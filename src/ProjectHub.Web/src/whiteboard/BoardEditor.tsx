import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import * as Y from 'yjs'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { fetchTasks, taskStatuses, type Task } from '../tasks/api'
import { fetchWhiteboardTasks, type Whiteboard, type WhiteboardTask } from './api'
import {
  addConnector,
  addObject,
  addNextTo,
  addObjects,
  boundsOf,
  colors,
  isColor,
  describe,
  describeArrow,
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
import {
  ArrowIcon,
  ConnectIcon,
  DiamondIcon,
  EllipseIcon,
  FitIcon,
  LockIcon,
  MinusIcon,
  PencilIcon,
  PlusIcon,
  RectIcon,
  RedoIcon,
  TaskIcon,
  TemplatesIcon,
  TextIcon,
  TrashIcon,
  UndoIcon,
  UnlockIcon,
} from './icons'
import { diamondPoints, fills, strokes, textColor } from './palette'
import { StickyStack, stickyDragType } from './StickyStack'
import { connectWhiteboard, type Peer, type SyncStatus, type WhiteboardSync } from './sync'
import { TemplatePicker } from './TemplatePicker'
import { InlineEdit } from '../ui/InlineEdit'
import { Menu } from '../ui/Menu'
import type { Template } from './templates'

type Props = {
  board: Whiteboard
  project: ProjectDetails
  me: Me
  revision: number
  onRename: (name: string) => Promise<void>
  onDelete: () => void
}

type View = { x: number; y: number; zoom: number }

type Drag =
  | { kind: 'pan'; startX: number; startY: number; view: View }
  | { kind: 'move' | 'resize' | 'start' | 'end'; startX: number; startY: number; object: BoardObject }
  /** Dragging a connector out of an object, like in draw.io: `at` is the current point on the board. */
  | { kind: 'connect'; startX: number; startY: number; object: BoardObject; at: { x: number; y: number }; overId: string | null }

/** Where the menu for an object opened, in pixels inside the canvas. */
type ObjectMenu = { id: string; x: number; y: number }

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
export function BoardEditor({ board, project, me, revision, onRename, onDelete }: Props) {
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
  const [editingId, setEditingId] = useState<string | null>(null)
  const [menu, setMenu] = useState<ObjectMenu | null>(null)
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
    setMenu((current) => (current && current.id === id ? current : null))
    setEditingId((current) => (current === id ? current : null))
    syncRef.current?.sendPresence(null, null, id)
  }

  /** Opens the text of an object for typing right on the canvas, like a double click in Miro. */
  function startEditing(id: string) {
    select(id)
    setEditingId(id)
  }

  /** Ends typing in one object; a blur from a new note opening elsewhere leaves that note open. */
  function stopEditing(id: string, backToCanvas: boolean) {
    undoRef.current?.stopCapturing()
    setEditingId((current) => (current === id ? null : current))
    if (backToCanvas) svgRef.current?.focus({ preventScroll: true })
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
    startEditing(ids[0])
  }

  function addStickyNextTo(object: BoardObject, direction: 'right' | 'below') {
    undoRef.current?.stopCapturing()
    const id = addNextTo(doc(), object, direction)
    undoRef.current?.stopCapturing()
    startEditing(id)
  }

  function change(id: string, changes: Changes) {
    updateObject(doc(), id, changes)
  }

  /** The topmost object under a point on the board; arrows and the object itself are skipped. */
  function objectAt(point: { x: number; y: number }, exceptId?: string): BoardObject | null {
    for (let i = objects.length - 1; i >= 0; i--) {
      const o = objects[i]
      if (o.type === 'arrow' || o.id === exceptId) continue
      if (point.x >= o.x && point.x <= o.x + o.w && point.y >= o.y && point.y <= o.y + o.h) return o
    }
    return null
  }

  /** Opens the menu of an object at a point in the page (right click, or the keyboard menu key). */
  function openMenu(id: string, clientX: number, clientY: number) {
    const rect = svgRef.current?.getBoundingClientRect()
    select(id)
    setMenu({ id, x: clientX - (rect?.left ?? 0), y: clientY - (rect?.top ?? 0) })
  }

  function toggleLock(object: BoardObject) {
    change(object.id, { locked: !object.locked })
    undoRef.current?.stopCapturing()
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
    // Without preventScroll the canvas scrolls into view under the pointer and a double click hits the background.
    svgRef.current?.focus({ preventScroll: true })
    if (canEdit && !object.locked) startDrag(event, { kind: 'move', startX: event.clientX, startY: event.clientY, object })
    else event.stopPropagation()
  }

  function onHandleDown(event: PointerEvent, object: BoardObject, kind: 'resize' | 'start' | 'end') {
    if (event.button !== 0 || !canEdit || object.locked) return
    // An arrow end that hangs on an object is taken off it as soon as it is dragged by hand.
    if (kind === 'start' && object.from) change(object.id, { from: null })
    if (kind === 'end' && object.to) change(object.id, { to: null })
    startDrag(event, { kind, startX: event.clientX, startY: event.clientY, object })
  }

  /** Starts a new arrow at one of the four dots around the selected object. */
  function onConnectDown(event: PointerEvent, object: BoardObject, at: { x: number; y: number }) {
    if (event.button !== 0 || !canEdit) return
    startDrag(event, { kind: 'connect', startX: event.clientX, startY: event.clientY, object, at, overId: null })
  }

  function onBackgroundDown(event: PointerEvent) {
    if (event.button !== 0) return
    select(null)
    setMenu(null)
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
    if (drag.kind === 'connect') {
      const at = toWorld(event.clientX, event.clientY)
      setDrag({ ...drag, at, overId: objectAt(at, o.id)?.id ?? null })
    } else if (drag.kind === 'move') change(o.id, moveBy(o, wx, wy))
    else if (drag.kind === 'resize') change(o.id, { w: o.w + wx, h: o.h + wy })
    else if (drag.kind === 'start') change(o.id, { x: o.x + wx, y: o.y + wy })
    else change(o.id, { x2: o.x2 + wx, y2: o.y2 + wy })
  }

  function onPointerUp() {
    if (drag?.kind === 'connect') {
      const target = drag.overId ? objects.find((o) => o.id === drag.overId) : null
      // A connector dropped on the object it started at would be a line of length zero.
      const id = addConnector(doc(), drag.object, target ?? drag.at)
      undoRef.current?.stopCapturing()
      select(id)
    }
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
      if (menu) setMenu(null)
      else select(null)
      return
    }
    // Shift+F10 and the menu key open the object's menu, as they do everywhere else.
    if (selected && (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10'))) {
      event.preventDefault()
      const rect = svgRef.current?.getBoundingClientRect()
      openMenu(selected.id, (rect?.left ?? 0) + view.x + (selected.x + selected.w / 2) * view.zoom, (rect?.top ?? 0) + view.y + selected.y * view.zoom)
      return
    }
    if (canEdit && !mod && !event.altKey && event.key.toLowerCase() === 'n') {
      event.preventDefault()
      addSticky()
      return
    }
    if (!selected || !canEdit || selected.locked) return
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault()
      remove(selected.id)
      return
    }
    if ((event.key === 'Enter' || event.key === 'F2') && hasText(selected)) {
      event.preventDefault()
      startEditing(selected.id)
      return
    }
    const step = event.shiftKey ? 1 : 10
    const deltas: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
    const delta = deltas[event.key]
    if (delta) {
      event.preventDefault()
      const [dx, dy] = delta
      // Alt with the arrow keys resizes, so the keyboard can do everything the handles do.
      if (!event.altKey) change(selected.id, moveBy(selected, dx, dy))
      else if (selected.type === 'arrow') change(selected.id, { x2: selected.x2 + dx, y2: selected.y2 + dy })
      else change(selected.id, { w: selected.w + dx, h: selected.h + dy })
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

  const menuObject = menu ? (objects.find((o) => o.id === menu.id) ?? null) : null
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
            aria-label={`Whiteboard ${board.name}. Objekt mit Klick wählen und ziehen, Rechtsklick öffnet die Optionen, Doppelklick oder Enter bearbeitet den Text. Von den Punkten am gewählten Objekt zieht man einen Pfeil zu einem anderen Objekt. Pfeiltasten verschieben, Alt+Pfeiltasten ändern die Größe, Entf löscht, N legt eine Notiz an, Strg+Z macht rückgängig. Gesperrte Objekte bleiben, wo sie sind. Die Objektliste rechts wählt Objekte per Tastatur.`}
            tabIndex={0}
            onPointerDown={onBackgroundDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerUp}
            onPointerLeave={onPointerLeave}
            onKeyDown={onKeyDown}
            onContextMenu={(event) => {
              // Right click belongs to the object, not to the browser: it opens the object's options.
              const target = objectAt(toWorld(event.clientX, event.clientY))
              event.preventDefault()
              if (target) openMenu(target.id, event.clientX, event.clientY)
              else setMenu(null)
            }}
            // Pointer capture makes the canvas the target of the double click; the press before it selected the object.
            onDoubleClick={() => {
              if (selected && canEdit && hasText(selected)) startEditing(selected.id)
            }}
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
                  editing={object.id === editingId && canEdit}
                  connectTarget={drag?.kind === 'connect' && drag.overId === object.id}
                  markerId={`arrow-head-${board.id}`}
                  onDown={(event) => onObjectDown(event, object)}
                  onHandleDown={(event, kind) => onHandleDown(event, object, kind)}
                  onConnectDown={(event, at) => onConnectDown(event, object, at)}
                  onAddNext={(direction) => addStickyNextTo(object, direction)}
                  onText={(text) => change(object.id, { text })}
                  onStopEditing={(backToCanvas) => stopEditing(object.id, backToCanvas)}
                />
              ))}
              {drag?.kind === 'connect' && (
                <line
                  className="board-connect-preview"
                  x1={drag.object.x + drag.object.w / 2}
                  y1={drag.object.y + drag.object.h / 2}
                  x2={drag.at.x}
                  y2={drag.at.y}
                  markerEnd={`url(#arrow-head-${board.id})`}
                />
              )}
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
          {objects.length === 0 && canEdit && !showTemplates && (
            <div className="board-empty">
              <p>Leeres Whiteboard. Drück N für eine Notiz oder</p>
              <button type="button" onClick={() => setShowTemplates(true)}>
                Mit einer Vorlage starten
              </button>
            </div>
          )}
          {menuObject && !drag && (
            <ObjectMenuCard
              key={menuObject.id}
              object={menuObject}
              at={menu!}
              editable={canEdit}
              onColor={(color) => {
                change(menuObject.id, { color })
                undoRef.current?.stopCapturing()
              }}
              onEditText={() => {
                setMenu(null)
                startEditing(menuObject.id)
              }}
              onLock={() => {
                toggleLock(menuObject)
                setMenu(null)
              }}
              onRemove={() => {
                setMenu(null)
                remove(menuObject.id)
              }}
              onClose={() => {
                setMenu(null)
                svgRef.current?.focus({ preventScroll: true })
              }}
            />
          )}
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
          <ObjectList objects={objects} tasks={tasks} selectedId={selectedId} onSelect={select} />
          {selected && (
            <ObjectPanel
              key={selected.id}
              object={selected}
              objects={objects}
              task={selected.taskId ? tasks.get(selected.taskId) : undefined}
              editable={canEdit}
              onColor={(color) => {
                change(selected.id, { color })
                undoRef.current?.stopCapturing()
              }}
              onEditText={() => startEditing(selected.id)}
              onLock={() => toggleLock(selected)}
              onRemove={() => remove(selected.id)}
            />
          )}
        </aside>
      </div>
    </div>
  )
}

type HeaderProps = { board: Whiteboard; canManage: boolean; onRename: (name: string) => Promise<void>; onDelete: () => void; children: ReactNode }

function BoardHeader({ board, canManage, onRename, onDelete, children }: HeaderProps) {
  return (
    <header className="board-header">
      <h3>
        <InlineEdit key={board.name} value={board.name} label="Name des Whiteboards" editable={canManage} onSave={onRename} />
      </h3>
      {children}
      {canManage && <Menu label="Weitere Aktionen zum Whiteboard" items={[{ label: 'Whiteboard löschen', danger: true, onSelect: onDelete }]} />}
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
  editing: boolean
  connectTarget: boolean
  markerId: string
  onDown: (event: PointerEvent) => void
  onHandleDown: (event: PointerEvent, kind: 'resize' | 'start' | 'end') => void
  onConnectDown: (event: PointerEvent, at: { x: number; y: number }) => void
  onAddNext: (direction: 'right' | 'below') => void
  onText: (text: string) => void
  onStopEditing: (backToCanvas: boolean) => void
}

/** Notes, shapes and text boxes carry text; arrows and task cards do not. */
const hasText = (object: BoardObject) => object.type !== 'arrow' && object.type !== 'task'

const textClass: Record<Exclude<ObjectType, 'arrow' | 'task'>, string> = {
  sticky: 'board-text',
  rect: 'board-text frame',
  ellipse: 'board-text centered',
  diamond: 'board-text centered',
  text: 'board-text large',
}

function ObjectShape({ object, task, selected, peerSelected, editable, editing, connectTarget, markerId, onDown, onHandleDown, onConnectDown, onAddNext, onText, onStopEditing }: ShapeProps) {
  const { x, y, w, h } = object
  const fill = fills[object.color]
  const stroke = strokes[object.color]
  const outline = connectTarget ? 'var(--accent)' : selected ? 'var(--accent)' : peerSelected ? peerColor(peerSelected.userId) : null
  const changeable = editable && !object.locked

  if (object.type === 'arrow') {
    return (
      <g className="board-object" data-object-id={object.id} onPointerDown={onDown}>
        <line x1={x} y1={y} x2={object.x2} y2={object.y2} stroke="transparent" strokeWidth={14} />
        <line x1={x} y1={y} x2={object.x2} y2={object.y2} stroke={outline ?? stroke} strokeWidth={selected ? 3 : 2} markerEnd={`url(#${markerId})`} />
        {selected && changeable && (
          <>
            <circle className="board-handle" cx={x} cy={y} r={6} onPointerDown={(event) => onHandleDown(event, 'start')} />
            <circle className="board-handle" cx={object.x2} cy={object.y2} r={6} onPointerDown={(event) => onHandleDown(event, 'end')} />
          </>
        )}
        {object.locked && <LockMark x={(x + object.x2) / 2} y={(y + object.y2) / 2} />}
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
    <g className={object.locked ? 'board-object locked' : 'board-object'} data-object-id={object.id} data-locked={object.locked || undefined} onPointerDown={onDown}>
      {shape}
      <foreignObject x={x} y={y} width={w} height={h} pointerEvents={editing ? undefined : 'none'}>
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
        ) : editing ? (
          <textarea
            className={`${textClass[object.type]} board-text-input`}
            style={object.type === 'text' ? undefined : { color: textColor(object.color) }}
            aria-label="Text"
            value={object.text}
            maxLength={2000}
            autoFocus
            onFocus={(event) => event.currentTarget.setSelectionRange(object.text.length, object.text.length)}
            onChange={(event) => onText(event.target.value)}
            onBlur={() => onStopEditing(false)}
            onPointerDown={(event) => event.stopPropagation()}
            onKeyDown={(event) => {
              // Typing stays in the note: Delete, N or the arrow keys must not reach the canvas shortcuts.
              event.stopPropagation()
              if (event.key === 'Escape' || (event.key === 'Enter' && (event.ctrlKey || event.metaKey))) {
                event.preventDefault()
                onStopEditing(true)
              }
            }}
          />
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
      {selected && changeable && <circle className="board-handle resize" cx={x + w + 5} cy={y + h + 5} r={5} onPointerDown={(event) => onHandleDown(event, 'resize')} />}
      {selected && changeable && (
        <>
          <ConnectDot cx={x + w / 2} cy={y - 12} label="Pfeil nach oben ziehen" onDown={(event) => onConnectDown(event, { x: x + w / 2, y: y - 12 })} />
          <ConnectDot cx={x + w + 12} cy={y + h / 2} label="Pfeil nach rechts ziehen" onDown={(event) => onConnectDown(event, { x: x + w + 12, y: y + h / 2 })} />
          <ConnectDot cx={x + w / 2} cy={y + h + 12} label="Pfeil nach unten ziehen" onDown={(event) => onConnectDown(event, { x: x + w / 2, y: y + h + 12 })} />
          <ConnectDot cx={x - 12} cy={y + h / 2} label="Pfeil nach links ziehen" onDown={(event) => onConnectDown(event, { x: x - 12, y: y + h / 2 })} />
        </>
      )}
      {selected && changeable && object.type === 'sticky' && (
        <>
          <QuickAdd cx={x + w + 30} cy={y + h - 10} label="Notiz rechts daneben" onAdd={() => onAddNext('right')} />
          <QuickAdd cx={x + w - 10} cy={y + h + 30} label="Notiz darunter" onAdd={() => onAddNext('below')} />
        </>
      )}
      {object.locked && <LockMark x={x + w - 10} y={y + 10} />}
    </g>
  )
}

/** A small lock on an object that cannot be moved. */
function LockMark({ x, y }: { x: number; y: number }) {
  return (
    <g className="board-lock-mark" transform={`translate(${x - 7} ${y - 7})`} aria-hidden="true">
      <rect x="0" y="5" width="14" height="9" rx="2" />
      <path d="M3 5 V3.2 a4 4 0 0 1 8 0 V5" fill="none" />
    </g>
  )
}

/** draw.io's connector dots: dragging one out of the object draws an arrow to wherever it is dropped. */
function ConnectDot({ cx, cy, label, onDown }: { cx: number; cy: number; label: string; onDown: (event: PointerEvent) => void }) {
  return (
    <g className="board-connect-dot" onPointerDown={onDown}>
      <title>{label}</title>
      <circle cx={cx} cy={cy} r={9} fill="transparent" />
      <circle cx={cx} cy={cy} r={4.5} />
    </g>
  )
}

type ListProps = {
  objects: BoardObject[]
  tasks: Map<string, WhiteboardTask>
  selectedId: string | null
  onSelect: (id: string) => void
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

/** Every object as a button, so the board works with the keyboard and a screen reader too. */
function ObjectList({ objects, tasks, selectedId, onSelect }: ListProps) {
  const title = (taskId: string) => tasks.get(taskId)?.title
  return (
    <section className="board-list" aria-labelledby="board-objects-heading">
      <h4 id="board-objects-heading">Objekte ({objects.length})</h4>
      {objects.length === 0 ? (
        <p className="muted">Noch leer.</p>
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
                {object.locked && (
                  <span className="board-list-lock" title="Gesperrt">
                    <span className="visually-hidden">Gesperrt: </span>
                    <span aria-hidden="true">🔒</span>
                  </span>
                )}
                {object.type === 'arrow' ? describeArrow(object, objects, title) : describe(object, object.taskId ? title(object.taskId) : null)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

type ColorsProps = { color: Color; onPick: (color: Color) => void; inMenu?: boolean }

/** The sixteen board colors as swatches, used in the object menu and in the right column. */
function ColorChoice({ color, onPick, inMenu = false }: ColorsProps) {
  return (
    <div className="sticky-swatches" role="group" aria-label="Farbe">
      {(Object.entries(colors) as [Color, string][]).map(([value, label]) => (
        <button
          key={value}
          type="button"
          role={inMenu ? 'menuitemradio' : undefined}
          className="sticky-swatch"
          aria-label={label}
          aria-checked={inMenu ? value === color : undefined}
          aria-pressed={inMenu ? undefined : value === color}
          title={label}
          style={{ background: fills[value], borderColor: strokes[value] }}
          onClick={() => onPick(value)}
        />
      ))}
    </div>
  )
}

type MenuProps = {
  object: BoardObject
  at: { x: number; y: number }
  editable: boolean
  onColor: (color: Color) => void
  onEditText: () => void
  onLock: () => void
  onRemove: () => void
  onClose: () => void
}

/**
 * The options of an object, opened with a right click like in draw.io: color, text, lock and remove.
 * A left click only selects and moves, so nothing pops up while someone is arranging the board.
 */
function ObjectMenuCard({ object, at, editable, onColor, onEditText, onLock, onRemove, onClose }: MenuProps) {
  const [colorsOpen, setColorsOpen] = useState(false)
  const first = useRef<HTMLButtonElement>(null)
  const kind = objectTypes[object.type]

  useEffect(() => first.current?.focus(), [])

  if (!editable) return null

  return (
    <div
      className="board-objectmenu"
      role="menu"
      aria-label={`${kind} bearbeiten`}
      style={{ left: at.x, top: at.y }}
      onPointerDown={(event) => event.stopPropagation()}
      onContextMenu={(event) => event.preventDefault()}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation()
          if (colorsOpen) setColorsOpen(false)
          else onClose()
        }
      }}
    >
      {hasText(object) && (
        <button ref={first} type="button" role="menuitem" className="board-menu-item" onClick={onEditText}>
          <PencilIcon /> Text bearbeiten
        </button>
      )}
      {object.type !== 'task' && (
        <>
          <button
            type="button"
            role="menuitem"
            className="board-menu-item"
            aria-expanded={colorsOpen}
            ref={hasText(object) ? undefined : first}
            onClick={() => setColorsOpen(!colorsOpen)}
          >
            <span className="board-color-dot" style={{ background: fills[object.color], borderColor: strokes[object.color] }} /> Farbe: {colors[object.color]}
          </button>
          {colorsOpen && (
            <div className="board-menu-colors">
              <ColorChoice
                inMenu
                color={object.color}
                onPick={(value) => {
                  onColor(value)
                  setColorsOpen(false)
                }}
              />
            </div>
          )}
        </>
      )}
      <button type="button" role="menuitem" className="board-menu-item" ref={hasText(object) || object.type !== 'task' ? undefined : first} onClick={onLock}>
        {object.locked ? <UnlockIcon /> : <LockIcon />} {object.locked ? 'Entsperren' : 'Sperren'}
      </button>
      <button type="button" role="menuitem" className="board-menu-item danger" disabled={object.locked} onClick={onRemove}>
        <TrashIcon /> {kind} entfernen
      </button>
    </div>
  )
}

type PanelProps = {
  object: BoardObject
  objects: BoardObject[]
  task: WhiteboardTask | undefined
  editable: boolean
  onColor: (color: Color) => void
  onEditText: () => void
  onLock: () => void
  onRemove: () => void
}

/** The selected object in the right column: what it is, what it is connected to, and what can be done with it. */
function ObjectPanel({ object, objects, task, editable, onColor, onEditText, onLock, onRemove }: PanelProps) {
  const kind = objectTypes[object.type]
  const connected = objects.filter((o) => o.type === 'arrow' && (o.from === object.id || o.to === object.id))
  const name = (id: string | null) => {
    const end = id ? objects.find((o) => o.id === id) : undefined
    if (!end) return 'frei'
    const text = end.type === 'task' ? (end.taskId ? (task?.id === end.taskId ? task.title : objectTypes.task) : objectTypes.task) : end.text.trim()
    return text || objectTypes[end.type]
  }

  return (
    <section className="board-selected" aria-labelledby="board-selected-heading">
      <h4 id="board-selected-heading">{kind}</h4>
      {object.type === 'task' ? (
        task ? (
          <dl className="board-selected-task">
            <dt>Aufgabe</dt>
            <dd>{task.title}</dd>
            <dt>Status</dt>
            <dd>{taskStatuses[task.status]}</dd>
            <dt>Zuständig</dt>
            <dd>{task.assigneeName ?? 'Niemand'}</dd>
          </dl>
        ) : (
          <p className="muted">Aufgabe nicht verfügbar.</p>
        )
      ) : (
        <p className="board-selected-text">{object.text.trim() || <span className="muted">Kein Text</span>}</p>
      )}
      {connected.length > 0 && (
        <p className="muted">
          Verbunden: {connected.map((arrow) => (arrow.from === object.id ? `→ ${name(arrow.to)}` : `← ${name(arrow.from)}`)).join(', ')}
        </p>
      )}
      {object.locked && <p className="muted">Gesperrt. Dieses Objekt lässt sich nicht verschieben.</p>}
      {editable && (
        <div className="board-selected-actions">
          {hasText(object) && (
            <button type="button" className="board-tool" aria-label="Text bearbeiten" title="Text bearbeiten (Doppelklick)" disabled={object.locked} onClick={onEditText}>
              <PencilIcon />
            </button>
          )}
          <button
            type="button"
            className="board-tool"
            aria-label={object.locked ? `${kind} entsperren` : `${kind} sperren`}
            title={object.locked ? 'Entsperren' : 'Sperren'}
            aria-pressed={object.locked}
            onClick={onLock}
          >
            {object.locked ? <UnlockIcon /> : <LockIcon />}
          </button>
          <button type="button" className="board-tool" aria-label={`${kind} entfernen`} title={`${kind} entfernen (Entf)`} disabled={object.locked} onClick={onRemove}>
            <TrashIcon />
          </button>
        </div>
      )}
      {editable && object.type !== 'task' && !object.locked && <ColorChoice color={object.color} onPick={onColor} />}
      <p className="muted board-hint">
        <ConnectIcon /> Von den Punkten am Objekt lässt sich ein Pfeil zu einem anderen Objekt ziehen. Rechtsklick öffnet die Optionen.
      </p>
    </section>
  )
}
