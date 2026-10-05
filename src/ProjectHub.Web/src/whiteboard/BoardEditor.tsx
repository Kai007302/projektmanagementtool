import { useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import * as Y from 'yjs'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { fetchTasks, taskStatuses, type Task } from '../tasks/api'
import { TaskDetails } from '../tasks/TaskDetails'
import { fetchWhiteboardTasks, type Whiteboard, type WhiteboardTask } from './api'
import {
  addObject,
  addNextTo,
  addObjects,
  boundsOf,
  colors,
  isColor,
  describe,
  localOrigin,
  moveBy,
  objectsOf,
  objectTypes,
  readObjects,
  removeObject,
  stickyGrid,
  updateObject,
  type BoardObject,
  type Bounds,
  type Changes,
  type Color,
  type NewObject,
  type ObjectType,
} from './model'
import { ArrowIcon, DiamondIcon, EllipseIcon, FitIcon, MinusIcon, PlusIcon, RectIcon, RedoIcon, TaskIcon, TemplatesIcon, TextIcon, UndoIcon } from './icons'
import { diamondPoints, fills, strokes, textColor } from './palette'
import { StickyStack, stickyDragType } from './StickyStack'
import { connectWhiteboard, type Peer, type SyncStatus, type WhiteboardSync } from './sync'
import { TemplatePicker } from './TemplatePicker'
import type { Template } from './templates'

type Props = {
  board: Whiteboard
  project: ProjectDetails
  me: Me
  revision: number
  onChanged: () => void
  onRename: (name: string) => Promise<void>
  onDelete: () => void
}

type View = { x: number; y: number; zoom: number }

type Drag =
  | { kind: 'pan'; startX: number; startY: number; view: View }
  | { kind: 'move' | 'resize' | 'start' | 'end'; startX: number; startY: number; object: BoardObject }

const peerColors = ['#d7263d', '#1b998b', '#c05805', '#6a4c93', '#2e86ab', '#8a6d00']

const peerColor = (userId: string) => peerColors[[...userId].reduce((sum, c) => sum + c.charCodeAt(0), 0) % peerColors.length]

const statusText: Record<SyncStatus, string> = {
  connecting: 'Verbinde …',
  online: 'Verbunden',
  offline: 'Offline. Änderungen werden gesendet, sobald die Verbindung zurück ist.',
}

const addable: { type: ObjectType; icon: () => ReactNode }[] = [
  { type: 'rect', icon: RectIcon },
  { type: 'ellipse', icon: EllipseIcon },
  { type: 'diamond', icon: DiamondIcon },
  { type: 'text', icon: TextIcon },
  { type: 'arrow', icon: ArrowIcon },
]

const stickyColorKey = 'projecthub.stickyColor'

/** The last sticky color someone picked is remembered in this browser, like in Miro. */
function rememberedStickyColor(): Color {
  try {
    const stored = localStorage.getItem(stickyColorKey)
    return isColor(stored) ? stored : 'yellow'
  } catch {
    return 'yellow'
  }
}

const minZoom = 0.25
const maxZoom = 3
const clampZoom = (zoom: number) => Math.min(maxZoom, Math.max(minZoom, zoom))

/** One whiteboard: canvas, toolbar, object list and properties, live with everyone else on it. */
export function BoardEditor({ board, project, me, revision, onChanged, onRename, onDelete }: Props) {
  const [generation, setGeneration] = useState(0)
  const [objects, setObjects] = useState<BoardObject[]>([])
  const [status, setStatus] = useState<SyncStatus>('connecting')
  const [serverCanEdit, setServerCanEdit] = useState(false)
  const [peers, setPeers] = useState<Map<string, Peer>>(new Map())
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<View>({ x: 0, y: 0, zoom: 1 })
  const [drag, setDrag] = useState<Drag | null>(null)
  const [tasks, setTasks] = useState<Map<string, WhiteboardTask>>(new Map())
  const [message, setMessage] = useState<string | null>(null)
  const [undoState, setUndoState] = useState({ canUndo: false, canRedo: false })
  const [showTemplates, setShowTemplates] = useState(false)
  const [stickyColor, setStickyColor] = useState<Color>(rememberedStickyColor)
  const [focusTextOf, setFocusTextOf] = useState<string | null>(null)
  const docRef = useRef<Y.Doc | null>(null)
  const undoRef = useRef<Y.UndoManager | null>(null)
  const syncRef = useRef<WhiteboardSync | null>(null)
  const svgRef = useRef<SVGSVGElement>(null)
  const lastPresence = useRef(0)

  const canEdit = serverCanEdit && project.capabilities.canContribute

  // One Yjs document per board and generation; a refused change starts a fresh generation.
  useEffect(() => {
    const doc = new Y.Doc()
    const undo = new Y.UndoManager(objectsOf(doc), { trackedOrigins: new Set([localOrigin]) })
    const refresh = () => setObjects(readObjects(doc))
    const refreshUndo = () => setUndoState({ canUndo: undo.canUndo(), canRedo: undo.canRedo() })
    objectsOf(doc).observeDeep(refresh)
    undo.on('stack-item-added', refreshUndo)
    undo.on('stack-item-popped', refreshUndo)
    docRef.current = doc
    undoRef.current = undo

    const sync = connectWhiteboard(board.id, doc, {
      onStatus: (next, editable) => {
        setStatus(next)
        setServerCanEdit(editable)
      },
      onPeer: (peer) => setPeers((current) => new Map(current).set(peer.connectionId, peer)),
      onPeerLeft: (connectionId) =>
        setPeers((current) => {
          const next = new Map(current)
          next.delete(connectionId)
          return next
        }),
      onRejected: () => {
        setMessage('Eine Änderung wurde nicht gespeichert. Das Whiteboard wurde neu geladen.')
        setGeneration((g) => g + 1)
      },
    })
    syncRef.current = sync

    return () => {
      sync.stop()
      undo.destroy()
      doc.destroy()
    }
  }, [board.id, generation])

  // Task cards show live task data from the API; reloaded when tasks change anywhere in the project.
  const taskIds = useMemo(() => [...new Set(objects.flatMap((o) => (o.type === 'task' && o.taskId ? [o.taskId] : [])))].sort(), [objects])
  const taskKey = taskIds.join(',')
  useEffect(() => {
    if (!taskKey) return
    fetchWhiteboardTasks(board.id, taskKey.split(',')).then(
      (list) => setTasks(new Map(list.map((t) => [t.id, t]))),
      () => {},
    )
  }, [board.id, taskKey, revision])

  // Wheel zoom needs a non-passive listener to keep the page from scrolling.
  useEffect(() => {
    const svg = svgRef.current
    if (!svg) return
    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      const rect = svg.getBoundingClientRect()
      const px = event.clientX - rect.left
      const py = event.clientY - rect.top
      setView((current) => {
        const zoom = clampZoom(current.zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1))
        // Keep the point under the pointer where it is.
        return { zoom, x: px - ((px - current.x) * zoom) / current.zoom, y: py - ((py - current.y) * zoom) / current.zoom }
      })
    }
    svg.addEventListener('wheel', onWheel, { passive: false })
    return () => svg.removeEventListener('wheel', onWheel)
  }, [])

  const selected = objects.find((o) => o.id === selectedId) ?? null
  const doc = () => docRef.current!

  const toWorld = (clientX: number, clientY: number) => {
    const rect = svgRef.current?.getBoundingClientRect()
    return { x: (clientX - (rect?.left ?? 0) - view.x) / view.zoom, y: (clientY - (rect?.top ?? 0) - view.y) / view.zoom }
  }

  const viewCenter = () => {
    const svg = svgRef.current
    return toWorld((svg?.getBoundingClientRect().left ?? 0) + (svg?.clientWidth ?? 600) / 2, (svg?.getBoundingClientRect().top ?? 0) + (svg?.clientHeight ?? 400) / 2)
  }

  function select(id: string | null) {
    setSelectedId(id)
    setFocusTextOf(null)
    syncRef.current?.sendPresence(null, null, id)
  }

  function add(type: ObjectType, taskId?: string) {
    const id = addObject(doc(), type, viewCenter(), taskId)
    undoRef.current?.stopCapturing()
    select(id)
  }

  /**
   * Places several objects as one undo step: in the middle of the view on an empty board, otherwise to the right of
   * everything already there so nothing is covered. Then zooms so all of it is visible.
   */
  function place(items: NewObject[]) {
    undoRef.current?.stopCapturing()
    const size = boundsOf(items)
    const existing = objects.length > 0 ? boundsOf(objects) : null
    const center = existing ? { x: existing.x + existing.w + 120 + size.w / 2, y: existing.y + size.h / 2 } : viewCenter()
    const { bounds } = addObjects(doc(), items, center)
    undoRef.current?.stopCapturing()
    select(null)
    fitTo(bounds)
    svgRef.current?.focus()
  }

  /** Zooms (at most to 100 %) and pans so the box fills the canvas, clear of the floating tools. */
  function fitTo(bounds: Bounds) {
    const svg = svgRef.current
    const width = svg?.clientWidth || 600
    const height = svg?.clientHeight || 400
    const free = { left: canEdit ? 80 : 24, right: 24, top: 24, bottom: 72 }
    const areaW = width - free.left - free.right
    const areaH = height - free.top - free.bottom
    const zoom = clampZoom(Math.min(1, areaW / Math.max(bounds.w, 1), areaH / Math.max(bounds.h, 1)))
    setView({
      zoom,
      x: free.left + areaW / 2 - (bounds.x + bounds.w / 2) * zoom,
      y: free.top + areaH / 2 - (bounds.y + bounds.h / 2) * zoom,
    })
  }

  function insertTemplate(template: Template) {
    setShowTemplates(false)
    place(template.items)
  }

  function chooseStickyColor(color: Color) {
    setStickyColor(color)
    try {
      localStorage.setItem(stickyColorKey, color)
    } catch {
      // Remembering the color is a convenience only.
    }
  }

  /**
   * An empty sticky note centered on the point (default: the middle of the view), ready for typing. Notes added
   * one after another at the same spot fan out instead of hiding each other.
   */
  function addSticky(center = viewCenter(), color = stickyColor) {
    undoRef.current?.stopCapturing()
    const at = { ...center }
    for (let i = 0; i < 20 && objects.some((o) => Math.abs(o.x + o.w / 2 - at.x) < 12 && Math.abs(o.y + o.h / 2 - at.y) < 12); i++) {
      at.x += 24
      at.y += 24
    }
    const { ids } = addObjects(doc(), [{ type: 'sticky', x: 0, y: 0, color, text: '' }], at)
    undoRef.current?.stopCapturing()
    select(ids[0])
    setFocusTextOf(ids[0])
  }

  function addStickyNextTo(object: BoardObject, direction: 'right' | 'below') {
    undoRef.current?.stopCapturing()
    const id = addNextTo(doc(), object, direction)
    undoRef.current?.stopCapturing()
    select(id)
    setFocusTextOf(id)
  }

  function change(id: string, changes: Changes) {
    updateObject(doc(), id, changes)
  }

  function remove(id: string) {
    removeObject(doc(), id)
    undoRef.current?.stopCapturing()
    if (selectedId === id) select(null)
  }

  function startDrag(event: PointerEvent, next: Drag) {
    event.stopPropagation()
    svgRef.current?.setPointerCapture?.(event.pointerId)
    undoRef.current?.stopCapturing()
    setDrag(next)
  }

  function onObjectDown(event: PointerEvent, object: BoardObject) {
    if (event.button !== 0) return
    select(object.id)
    svgRef.current?.focus()
    if (canEdit) startDrag(event, { kind: 'move', startX: event.clientX, startY: event.clientY, object })
    else event.stopPropagation()
  }

  function onHandleDown(event: PointerEvent, object: BoardObject, kind: 'resize' | 'start' | 'end') {
    if (event.button !== 0 || !canEdit) return
    startDrag(event, { kind, startX: event.clientX, startY: event.clientY, object })
  }

  function onBackgroundDown(event: PointerEvent) {
    if (event.button !== 0) return
    select(null)
    svgRef.current?.setPointerCapture?.(event.pointerId)
    setDrag({ kind: 'pan', startX: event.clientX, startY: event.clientY, view })
  }

  function onPointerMove(event: PointerEvent) {
    if (event.timeStamp - lastPresence.current > 50) {
      lastPresence.current = event.timeStamp
      const point = toWorld(event.clientX, event.clientY)
      syncRef.current?.sendPresence(Math.round(point.x), Math.round(point.y), selectedId)
    }

    if (!drag) return
    const dx = event.clientX - drag.startX
    const dy = event.clientY - drag.startY
    if (drag.kind === 'pan') {
      setView({ ...drag.view, x: drag.view.x + dx, y: drag.view.y + dy })
      return
    }

    const wx = dx / view.zoom
    const wy = dy / view.zoom
    const o = drag.object
    if (drag.kind === 'move') change(o.id, moveBy(o, wx, wy))
    else if (drag.kind === 'resize') change(o.id, { w: o.w + wx, h: o.h + wy })
    else if (drag.kind === 'start') change(o.id, { x: o.x + wx, y: o.y + wy })
    else change(o.id, { x2: o.x2 + wx, y2: o.y2 + wy })
  }

  function onPointerUp() {
    if (drag && drag.kind !== 'pan') undoRef.current?.stopCapturing()
    setDrag(null)
  }

  function onPointerLeave() {
    syncRef.current?.sendPresence(null, null, selectedId)
  }

  function onKeyDown(event: KeyboardEvent) {
    const mod = event.ctrlKey || event.metaKey
    if (mod && event.key.toLowerCase() === 'z') {
      event.preventDefault()
      if (event.shiftKey) undoRef.current?.redo()
      else undoRef.current?.undo()
      return
    }
    if (mod && event.key.toLowerCase() === 'y') {
      event.preventDefault()
      undoRef.current?.redo()
      return
    }
    if (event.key === 'Escape') {
      select(null)
      return
    }
    if (canEdit && !mod && !event.altKey && event.key.toLowerCase() === 'n') {
      event.preventDefault()
      addSticky()
      return
    }
    if (!selected || !canEdit) return
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault()
      remove(selected.id)
      return
    }
    const step = event.shiftKey ? 1 : 10
    const deltas: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
    const delta = deltas[event.key]
    if (delta) {
      event.preventDefault()
      change(selected.id, moveBy(selected, delta[0], delta[1]))
    }
  }

  const zoomBy = (factor: number) =>
    setView((current) => {
      const svg = svgRef.current
      const cx = (svg?.clientWidth ?? 600) / 2
      const cy = (svg?.clientHeight ?? 400) / 2
      const zoom = clampZoom(current.zoom * factor)
      return { zoom, x: cx - ((cx - current.x) * zoom) / current.zoom, y: cy - ((cy - current.y) * zoom) / current.zoom }
    })

  const peerList = [...peers.values()]
  const present = [...new Map([[me.id, me.displayName] as const, ...peerList.map((p) => [p.userId, p.name] as const)]).values()]

  return (
    <div className="board">
      <BoardHeader board={board} canManage={project.capabilities.canEdit} onRename={onRename} onDelete={onDelete}>
        <p className="board-presence" role="status">
          <span className={`board-presence-dot ${status}`} aria-hidden="true" />
          {statusText[status]}
          {status === 'online' && !canEdit && ' · Nur ansehen'}
          {' · Gerade dabei: '}
          {present.join(', ')}
        </p>
      </BoardHeader>
      {message && (
        <p role="alert" className="board-message">
          {message}{' '}
          <button type="button" className="link-button" onClick={() => setMessage(null)}>
            Ausblenden
          </button>
        </p>
      )}
      <div className="board-layout">
        <div className="board-stage">
          <svg
            ref={svgRef}
            className={drag?.kind === 'pan' ? 'board-canvas panning' : 'board-canvas'}
            role="application"
            aria-roledescription="Whiteboard"
            aria-label={`Whiteboard ${board.name}. Objekt mit Klick wählen und ziehen. Pfeiltasten verschieben, Entf löscht, N legt eine Notiz an, Strg+Z macht rückgängig. Die Objektliste daneben bietet dasselbe per Formular.`}
            tabIndex={0}
            onPointerDown={onBackgroundDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerUp}
            onPointerLeave={onPointerLeave}
            onKeyDown={onKeyDown}
            onDragOver={(event) => {
              if (canEdit && event.dataTransfer.types.includes(stickyDragType)) {
                event.preventDefault()
                event.dataTransfer.dropEffect = 'copy'
              }
            }}
            onDrop={(event) => {
              const color = event.dataTransfer.getData(stickyDragType)
              if (!canEdit || !color) return
              event.preventDefault()
              addSticky(toWorld(event.clientX, event.clientY), isColor(color) ? color : stickyColor)
            }}
          >
            <defs>
              <marker id={`arrow-head-${board.id}`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" fill="#5a6573" />
              </marker>
            </defs>
            <g transform={`translate(${view.x} ${view.y}) scale(${view.zoom})`}>
              {objects.map((object) => (
                <ObjectShape
                  key={object.id}
                  object={object}
                  task={object.taskId ? tasks.get(object.taskId) : undefined}
                  selected={object.id === selectedId}
                  peerSelected={peerList.find((p) => p.selectedObjectId === object.id)}
                  editable={canEdit}
                  markerId={`arrow-head-${board.id}`}
                  onDown={(event) => onObjectDown(event, object)}
                  onHandleDown={(event, kind) => onHandleDown(event, object, kind)}
                  onAddNext={(direction) => addStickyNextTo(object, direction)}
                />
              ))}
              {peerList.map((peer) =>
                peer.x === null || peer.y === null ? null : (
                  <g key={peer.connectionId} className="board-cursor" transform={`translate(${peer.x} ${peer.y}) scale(${1 / view.zoom})`} aria-hidden="true">
                    <path d="M 0 0 L 0 16 L 4 12 L 8 20 L 11 18 L 7 11 L 13 11 z" fill={peerColor(peer.userId)} stroke="#fff" strokeWidth="1" />
                    <text x="14" y="24" fill={peerColor(peer.userId)}>
                      {peer.name}
                    </text>
                  </g>
                ),
              )}
            </g>
          </svg>
          {canEdit && (
            <div className="board-tools" role="toolbar" aria-label="Werkzeuge" aria-orientation="vertical">
              <StickyStack color={stickyColor} onColor={chooseStickyColor} onAdd={() => addSticky()} onBulk={(lines) => place(stickyGrid(lines, stickyColor))} />
              <span className="board-tools-divider" aria-hidden="true" />
              {addable.map(({ type, icon: ToolIcon }) => (
                <button key={type} type="button" className="board-tool" aria-label={`${objectTypes[type]} hinzufügen`} title={objectTypes[type]} onClick={() => add(type)}>
                  <ToolIcon />
                </button>
              ))}
              <TaskAdder projectId={project.id} onAdd={(taskId) => add('task', taskId)} />
              <span className="board-tools-divider" aria-hidden="true" />
              <button type="button" className="board-tool" aria-label="Vorlagen" title="Vorlagen" aria-expanded={showTemplates} onClick={() => setShowTemplates(!showTemplates)}>
                <TemplatesIcon />
              </button>
            </div>
          )}
          {canEdit && showTemplates && <TemplatePicker onPick={insertTemplate} onClose={() => setShowTemplates(false)} />}
          <div className="board-corner board-corner-left">
            {canEdit && (
              <>
                <button type="button" className="board-tool" aria-label="Rückgängig" title="Rückgängig (Strg+Z)" disabled={!undoState.canUndo} onClick={() => undoRef.current?.undo()}>
                  <UndoIcon />
                </button>
                <button type="button" className="board-tool" aria-label="Wiederholen" title="Wiederholen (Strg+Y)" disabled={!undoState.canRedo} onClick={() => undoRef.current?.redo()}>
                  <RedoIcon />
                </button>
              </>
            )}
          </div>
          <div className="board-corner board-corner-right">
            <button type="button" className="board-tool" aria-label="Verkleinern" title="Verkleinern" onClick={() => zoomBy(1 / 1.25)}>
              <MinusIcon />
            </button>
            <button type="button" className="board-zoom-level" title="Ansicht zurücksetzen" aria-label={`${Math.round(view.zoom * 100)} %, Ansicht zurücksetzen`} onClick={() => setView({ x: 0, y: 0, zoom: 1 })}>
              {Math.round(view.zoom * 100)} %
            </button>
            <button type="button" className="board-tool" aria-label="Vergrößern" title="Vergrößern" onClick={() => zoomBy(1.25)}>
              <PlusIcon />
            </button>
            <button type="button" className="board-tool" aria-label="Alles zeigen" title="Alles zeigen" disabled={objects.length === 0} onClick={() => fitTo(boundsOf(objects))}>
              <FitIcon />
            </button>
          </div>
        </div>
        <aside className="board-side">
          <ObjectList objects={objects} tasks={tasks} selectedId={selectedId} onSelect={select} onTemplates={canEdit ? () => setShowTemplates(true) : undefined} />
          {selected && (
            <ObjectForm
              key={selected.id}
              object={selected}
              task={selected.taskId ? tasks.get(selected.taskId) : undefined}
              editable={canEdit}
              focusText={focusTextOf === selected.id}
              project={project}
              me={me}
              onChange={(changes) => {
                change(selected.id, changes)
              }}
              onCommit={() => undoRef.current?.stopCapturing()}
              onRemove={() => remove(selected.id)}
              onChanged={onChanged}
            />
          )}
        </aside>
      </div>
    </div>
  )
}

type HeaderProps = { board: Whiteboard; canManage: boolean; onRename: (name: string) => Promise<void>; onDelete: () => void; children: ReactNode }

function BoardHeader({ board, canManage, onRename, onDelete, children }: HeaderProps) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(board.name)

  async function submit(event: FormEvent) {
    event.preventDefault()
    await onRename(name)
    setEditing(false)
  }

  return (
    <header className="board-header">
      {editing ? (
        <form className="inline-form" onSubmit={submit}>
          <label>
            Name
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} autoFocus />
          </label>
          <button type="submit">Speichern</button>
          <button type="button" onClick={() => setEditing(false)}>
            Abbrechen
          </button>
        </form>
      ) : (
        <h3>{board.name}</h3>
      )}
      {children}
      {canManage && !editing && (
        <span className="board-header-actions">
          <button type="button" className="link-button" onClick={() => {
              setName(board.name)
              setEditing(true)
            }}>
            Umbenennen
          </button>{' '}
          <button type="button" className="link-button danger-link" onClick={onDelete}>
            Löschen
          </button>
        </span>
      )}
    </header>
  )
}

function TaskAdder({ projectId, onAdd }: { projectId: string; onAdd: (taskId: string) => void }) {
  const [open, setOpen] = useState(false)
  const [tasks, setTasks] = useState<Task[] | null>(null)
  const [taskId, setTaskId] = useState('')

  return (
    <span className="board-tool-group">
      <button
        type="button"
        className="board-tool"
        aria-label="Aufgabe als Karte"
        title="Aufgabe als Karte"
        aria-expanded={open}
        onClick={() => {
          if (!open && tasks === null) fetchTasks(projectId).then((page) => setTasks(page.items), () => setTasks([]))
          setOpen(!open)
        }}
      >
        <TaskIcon />
      </button>
      {open && (
        <div
          className="board-popover"
          role="group"
          aria-label="Aufgabe als Karte"
          onKeyDown={(event) => {
            if (event.key === 'Escape') setOpen(false)
          }}
        >
          <label>
            Aufgabe für eine Karte
            <select value={taskId} onChange={(event) => setTaskId(event.target.value)}>
              <option value="">{tasks === null ? 'Aufgaben werden geladen …' : 'Aufgabe wählen …'}</option>
              {tasks?.map((task) => (
                <option key={task.id} value={task.id}>
                  {task.title}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            disabled={!taskId}
            onClick={() => {
              onAdd(taskId)
              setTaskId('')
              setOpen(false)
            }}
          >
            + Aufgabe
          </button>
        </div>
      )}
    </span>
  )
}

type ShapeProps = {
  object: BoardObject
  task: WhiteboardTask | undefined
  selected: boolean
  peerSelected: Peer | undefined
  editable: boolean
  markerId: string
  onDown: (event: PointerEvent) => void
  onHandleDown: (event: PointerEvent, kind: 'resize' | 'start' | 'end') => void
  onAddNext: (direction: 'right' | 'below') => void
}

const textClass: Record<Exclude<ObjectType, 'arrow' | 'task'>, string> = {
  sticky: 'board-text',
  rect: 'board-text frame',
  ellipse: 'board-text centered',
  diamond: 'board-text centered',
  text: 'board-text large',
}

function ObjectShape({ object, task, selected, peerSelected, editable, markerId, onDown, onHandleDown, onAddNext }: ShapeProps) {
  const { x, y, w, h } = object
  const fill = fills[object.color]
  const stroke = strokes[object.color]
  const outline = selected ? 'var(--accent)' : peerSelected ? peerColor(peerSelected.userId) : null

  if (object.type === 'arrow') {
    return (
      <g className="board-object" data-object-id={object.id} onPointerDown={onDown}>
        <line x1={x} y1={y} x2={object.x2} y2={object.y2} stroke="transparent" strokeWidth={14} />
        <line x1={x} y1={y} x2={object.x2} y2={object.y2} stroke={outline ?? stroke} strokeWidth={selected ? 3 : 2} markerEnd={`url(#${markerId})`} />
        {selected && editable && (
          <>
            <circle className="board-handle" cx={x} cy={y} r={6} onPointerDown={(event) => onHandleDown(event, 'start')} />
            <circle className="board-handle" cx={object.x2} cy={object.y2} r={6} onPointerDown={(event) => onHandleDown(event, 'end')} />
          </>
        )}
      </g>
    )
  }

  const shape =
    object.type === 'ellipse' ? (
      <ellipse cx={x + w / 2} cy={y + h / 2} rx={w / 2} ry={h / 2} fill={fill} stroke={stroke} />
    ) : object.type === 'diamond' ? (
      <polygon points={diamondPoints(x, y, w, h)} fill={fill} stroke={stroke} />
    ) : object.type === 'text' ? (
      <rect x={x} y={y} width={w} height={h} fill="transparent" stroke={selected ? stroke : 'none'} strokeDasharray="4 3" />
    ) : (
      <rect
        x={x}
        y={y}
        width={w}
        height={h}
        rx={object.type === 'sticky' ? 3 : object.type === 'task' ? 10 : 8}
        fill={object.type === 'task' ? '#fff' : fill}
        stroke={object.type === 'sticky' ? 'none' : object.type === 'task' ? '#cbd2db' : stroke}
        className={object.type === 'sticky' ? 'board-sticky' : object.type === 'task' ? 'board-card' : undefined}
      />
    )

  return (
    <g className="board-object" data-object-id={object.id} onPointerDown={onDown}>
      {shape}
      <foreignObject x={x} y={y} width={w} height={h} pointerEvents="none">
        {object.type === 'task' ? (
          <div className="board-task">
            {task ? (
              <>
                <strong>{task.title}</strong>
                <span className="board-task-status">{taskStatuses[task.status]}</span>
                <span className="muted">{task.assigneeName ?? 'Nicht zugewiesen'}</span>
              </>
            ) : (
              <span className="muted">Aufgabe nicht verfügbar</span>
            )}
          </div>
        ) : (
          <div className={textClass[object.type]} style={object.type === 'text' ? undefined : { color: textColor(object.color) }}>
            {object.text}
          </div>
        )}
      </foreignObject>
      {outline && (
        <rect
          x={x - 5}
          y={y - 5}
          width={w + 10}
          height={h + 10}
          rx={8}
          fill="none"
          stroke={outline}
          strokeWidth={1.5}
          strokeDasharray={selected ? undefined : '6 4'}
          vectorEffect="non-scaling-stroke"
        />
      )}
      {selected && editable && <circle className="board-handle resize" cx={x + w + 5} cy={y + h + 5} r={5} onPointerDown={(event) => onHandleDown(event, 'resize')} />}
      {selected && editable && object.type === 'sticky' && (
        <>
          <QuickAdd cx={x + w + 26} cy={y + h / 2} label="Notiz rechts daneben" onAdd={() => onAddNext('right')} />
          <QuickAdd cx={x + w / 2} cy={y + h + 26} label="Notiz darunter" onAdd={() => onAddNext('below')} />
        </>
      )}
    </g>
  )
}

type ListProps = {
  objects: BoardObject[]
  tasks: Map<string, WhiteboardTask>
  selectedId: string | null
  onSelect: (id: string) => void
  onTemplates?: () => void
}

/** Miro's blue dots: a click puts a new note of the same color next to the selected one. */
function QuickAdd({ cx, cy, label, onAdd }: { cx: number; cy: number; label: string; onAdd: () => void }) {
  return (
    <g
      className="board-quick-add"
      onPointerDown={(event) => {
        event.stopPropagation()
        if (event.button === 0) onAdd()
      }}
    >
      <title>{label}</title>
      <circle cx={cx} cy={cy} r={9} />
      <path d={`M ${cx - 4} ${cy} H ${cx + 4} M ${cx} ${cy - 4} V ${cy + 4}`} />
    </g>
  )
}

function ObjectList({ objects, tasks, selectedId, onSelect, onTemplates }: ListProps) {
  return (
    <section aria-labelledby="board-objects-heading">
      <h4 id="board-objects-heading">Objekte ({objects.length})</h4>
      {objects.length === 0 ? (
        <p className="muted">
          Noch leer.
          {onTemplates && (
            <>
              {' '}
              <button type="button" className="link-button" onClick={onTemplates}>
                Mit einer Vorlage starten
              </button>
            </>
          )}
        </p>
      ) : (
        <ul className="board-object-list">
          {objects.map((object) => (
            <li key={object.id}>
              <button
                type="button"
                className={object.id === selectedId ? 'link-button selected' : 'link-button'}
                aria-pressed={object.id === selectedId}
                onClick={() => onSelect(object.id)}
              >
                {describe(object, object.taskId ? tasks.get(object.taskId)?.title : null)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

type FormProps = {
  object: BoardObject
  task: WhiteboardTask | undefined
  editable: boolean
  focusText: boolean
  project: ProjectDetails
  me: Me
  onChange: (changes: Changes) => void
  onCommit: () => void
  onRemove: () => void
  onChanged: () => void
}

function ObjectForm({ object, task, editable, focusText, project, me, onChange, onCommit, onRemove, onChanged }: FormProps) {
  const [details, setDetails] = useState(false)
  const number = (label: string, field: 'x' | 'y' | 'w' | 'h' | 'x2' | 'y2') => (
    <label>
      {label}
      <input
        type="number"
        value={Math.round(object[field])}
        disabled={!editable}
        onChange={(event) => {
          const value = event.target.valueAsNumber
          if (Number.isFinite(value)) onChange({ [field]: value })
        }}
        onBlur={onCommit}
      />
    </label>
  )

  return (
    <section className="board-form" aria-labelledby="board-form-heading">
      <h4 id="board-form-heading">{objectTypes[object.type]}</h4>
      {object.type === 'task' && (
        <>
          <p>{task ? `${task.title} · ${taskStatuses[task.status]}` : 'Die Aufgabe ist gelöscht oder für dich nicht sichtbar.'}</p>
          {task && (
            <button type="button" className="link-button" aria-expanded={details} onClick={() => setDetails(!details)}>
              {details ? 'Aufgabe schließen' : 'Aufgabe öffnen'}
            </button>
          )}
        </>
      )}
      {object.type !== 'task' && object.type !== 'arrow' && (
        <div className="board-text-field">
          {/* A separate label keeps the field's name "Text" instead of label plus content. */}
          <label htmlFor="board-object-text">Text</label>
          <textarea
            id="board-object-text"
            value={object.text}
            disabled={!editable}
            autoFocus={focusText}
            rows={3}
            maxLength={2000}
            onChange={(event) => onChange({ text: event.target.value })}
            onBlur={onCommit}
          />
        </div>
      )}
      {object.type !== 'task' && (
        <div className="board-color-field">
          <span id="board-color-label">Farbe: {colors[object.color]}</span>
          <div className="sticky-swatches" role="group" aria-labelledby="board-color-label">
            {(Object.entries(colors) as [Color, string][]).map(([value, label]) => (
              <button
                key={value}
                type="button"
                className="sticky-swatch"
                aria-label={label}
                aria-pressed={value === object.color}
                title={label}
                disabled={!editable}
                style={{ background: fills[value], borderColor: strokes[value] }}
                onClick={() => {
                  onChange({ color: value })
                  onCommit()
                }}
              />
            ))}
          </div>
        </div>
      )}
      <details className="board-geometry">
        <summary>Position und Größe</summary>
        <div className="board-fields">
          {number(object.type === 'arrow' ? 'Start X' : 'X', 'x')}
          {number(object.type === 'arrow' ? 'Start Y' : 'Y', 'y')}
          {object.type === 'arrow' ? (
            <>
              {number('Ende X', 'x2')}
              {number('Ende Y', 'y2')}
            </>
          ) : (
            <>
              {number('Breite', 'w')}
              {number('Höhe', 'h')}
            </>
          )}
        </div>
      </details>
      {editable && (
        <button type="button" className="danger" onClick={onRemove}>
          {objectTypes[object.type]} entfernen
        </button>
      )}
      {details && task && <TaskDetails taskId={task.id} project={project} me={me} onChanged={onChanged} onDeleted={() => {
            setDetails(false)
            onChanged()
          }} />}
    </section>
  )
}
