import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent, type PointerEvent } from 'react'
import { ApiError } from '../api/client'
import { CalendarActions } from '../calendar/CalendarActions'
import { downloadMilestoneCalendar } from '../calendar/calendar'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { taskStatuses, updateTask } from '../tasks/api'
import { TaskDetails } from '../tasks/TaskDetails'
import {
  createDependency,
  createMilestone,
  deleteDependency,
  deleteMilestone,
  downloadGanttPdf,
  dependencyTypes,
  fetchGantt,
  updateMilestone,
  type DependencyType,
  type Gantt,
  type GanttDependency,
  type GanttMilestone,
  type GanttTask,
} from './api'
import {
  anchorsOf,
  dayWidth,
  displayDay,
  dragged,
  formatDay,
  headerOf,
  isWeekend,
  parseDay,
  rangeOf,
  rowsOf,
  spanOf,
  today,
  zoomText,
  type Day,
  type DragMode,
  type Zoom,
} from './timeline'
import { useLatest } from '../api/useLatest'
import { EmptyState } from '../ui/EmptyState'
import { Skeleton } from '../ui/Skeleton'

type Props = { project: ProjectDetails; me: Me; revision: number; onChanged: () => void }

type Span = { start: Day; end: Day }

type Drag = { taskId: string; mode: DragMode; originX: number; delta: number }

const headerRow = 24
const headerHeight = headerRow * 2
const milestoneRow = 28
const rowHeight = 36
const barHeight = 20
const handleWidth = 6

const conflictText = 'Jemand anderes hat die Aufgabe inzwischen geändert. Der aktuelle Stand wurde geladen.'

/** Turns API errors into German messages for the most common cases. */
function messageOf(error: unknown): string {
  if (!(error instanceof ApiError)) return (error as Error).message
  if (error.message.includes('cycle')) return 'Diese Abhängigkeit würde einen Kreis bilden.'
  if (error.message.includes('already linked')) return 'Diese Aufgaben sind schon miteinander verknüpft.'
  if (error.message.includes('parent or subtask')) return 'Eine Aufgabe kann nicht von ihrer eigenen Ober- oder Unteraufgabe abhängen.'
  if (error.status === 409) return conflictText
  if (error.status === 403) return 'Dafür fehlt dir die Berechtigung.'
  return error.message
}

const spanText = (span: Span) =>
  span.start === span.end ? displayDay(span.start) : `${displayDay(span.start)} bis ${displayDay(span.end)}`

export function GanttChart({ project, me, revision, onChanged }: Props) {
  const [gantt, setGantt] = useState<Gantt | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [zoom, setZoom] = useState<Zoom>('day')
  const [selected, setSelected] = useState<string | null>(null)
  const [drag, setDrag] = useState<Drag | null>(null)
  const saving = useRef(false)
  const scroller = useRef<HTMLDivElement>(null)
  const { canContribute, canEdit } = project.capabilities

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchGantt(project.id)).then(setGantt, (e: Error) => setError(e.message))
  }, [latest, project.id])

  useEffect(load, [load, revision])

  const now = today()
  const rows = gantt ? rowsOf(gantt.tasks) : []
  const spans = new Map(rows.map(({ task }) => [task.id, spanOf(task)]))
  const scheduledDays = gantt
    ? [...[...spans.values()].flatMap((span) => (span ? [span.start, span.end] : [])), ...gantt.milestones.map((m) => parseDay(m.date))]
    : []
  const range = rangeOf(scheduledDays, zoom, now)
  const width = dayWidth[zoom]
  const x = (day: Day) => (day - range.start) * width
  const chartWidth = (range.end - range.start + 1) * width
  const rowTop = (index: number) => headerHeight + milestoneRow + index * rowHeight
  const chartHeight = rowTop(rows.length)
  const isLoaded = gantt !== null

  function scrollToToday() {
    const element = scroller.current
    if (element) element.scrollLeft = Math.max(0, (now - range.start) * width - element.clientWidth / 3)
  }

  // Start with today in view and keep it there when the zoom changes, but not after every edit.
  const scrollOnZoom = useRef(scrollToToday)
  useEffect(() => {
    scrollOnZoom.current = scrollToToday
  })
  useEffect(() => {
    if (isLoaded) scrollOnZoom.current()
  }, [isLoaded, zoom])

  /** The span a task is shown with, including a drag in progress. */
  function shownSpan(task: GanttTask): Span | null {
    const span = spans.get(task.id) ?? null
    return span && drag?.taskId === task.id ? dragged(span, drag.mode, drag.delta) : span
  }

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
      onChanged()
    } catch (e) {
      setError(messageOf(e))
    }
    load()
  }

  /** Saves new dates through the task API, showing them right away. */
  async function reschedule(task: GanttTask, span: Span | null) {
    if (saving.current) return
    const startDate = span ? formatDay(span.start) : null
    const dueDate = span ? formatDay(span.end) : null
    if (startDate === task.startDate && dueDate === task.dueDate) return
    saving.current = true
    const show = (changes: Partial<GanttTask>) => {
      // Loads still on their way predate this change and must not undo it.
      latest.invalidate()
      setGantt((current) => current && { ...current, tasks: current.tasks.map((t) => (t.id === task.id ? { ...t, ...changes } : t)) })
    }
    show({ startDate, dueDate })
    try {
      // The new version lets the next change follow right away, before the chart has reloaded.
      await run(async () => show({ version: (await updateTask(task.id, task.version, { startDate, dueDate })).version }))
    } finally {
      saving.current = false
    }
  }

  function pointerDown(event: PointerEvent<SVGGElement>, task: GanttTask) {
    if (event.button !== 0) return
    const handle = (event.target as Element).getAttribute('data-handle') as DragMode | null
    if (canContribute) event.currentTarget.setPointerCapture(event.pointerId)
    setDrag({ taskId: task.id, mode: handle ?? 'move', originX: event.clientX, delta: 0 })
  }

  function pointerMove(event: PointerEvent<SVGGElement>) {
    if (!drag || !canContribute) return
    const delta = Math.round((event.clientX - drag.originX) / width)
    if (delta !== drag.delta) setDrag({ ...drag, delta })
  }

  function pointerUp(task: GanttTask) {
    if (!drag) return
    const span = spans.get(task.id)
    setDrag(null)
    if (drag.delta === 0 || !span || !canContribute) {
      setSelected(task.id)
      return
    }
    void reschedule(task, dragged(span, drag.mode, drag.delta))
  }

  function barKey(event: KeyboardEvent<SVGGElement>, task: GanttTask) {
    const span = spans.get(task.id)
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      setSelected(task.id)
    } else if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && span && canContribute) {
      event.preventDefault()
      void reschedule(task, dragged(span, event.shiftKey ? 'end' : 'move', event.key === 'ArrowLeft' ? -1 : 1))
    }
  }

  if (!gantt) return error ? <p role="alert">{error}</p> : <Skeleton kind="list" count={5} label="Gantt wird geladen" />

  const rowIndex = new Map(rows.map((row, index) => [row.task.id, index]))
  const titles = new Map(gantt.tasks.map((task) => [task.id, task.title]))
  const violated = gantt.dependencies.filter((d) => d.violated)
  const header = headerOf(range, zoom)
  const selectedTask = gantt.tasks.find((task) => task.id === selected)

  return (
    <section className="panel gantt" aria-labelledby="gantt-heading">
      <header className="gantt-toolbar">
        <h3 id="gantt-heading">Gantt</h3>
        <div className="row" role="group" aria-label="Zoom">
          {(Object.keys(zoomText) as Zoom[]).map((value) => (
            <button key={value} type="button" aria-pressed={value === zoom} onClick={() => setZoom(value)}>
              {zoomText[value]}
            </button>
          ))}
          <button type="button" onClick={scrollToToday}>
            Heute
          </button>
          <button
            type="button"
            onClick={() => {
              setError(null)
              downloadGanttPdf(project.id, project.name).catch((e: Error) => setError(e.message))
            }}
          >
            Als PDF
          </button>
        </div>
      </header>
      {error && <p role="alert">{error}</p>}
      {canContribute && (
        <p className="muted gantt-hint">
          Balken ziehen verschiebt die Aufgabe, der rechte Rand ändert das Ende. Mit der Tastatur: Balken fokussieren, Pfeiltasten
          verschieben um einen Tag, mit Umschalt ändern sie das Ende.
        </p>
      )}
      {violated.length > 0 && (
        <section className="gantt-warnings" aria-label="Verletzte Abhängigkeiten">
          <p>
            <strong>{violated.length === 1 ? '1 Abhängigkeit ist verletzt' : `${violated.length} Abhängigkeiten sind verletzt`}</strong>
          </p>
          <ul>
            {violated.map((d) => (
              <li key={d.id}>
                „{titles.get(d.targetTaskId)}“ passt nicht zu „{titles.get(d.sourceTaskId)}“ ({dependencyTypes[d.dependencyType]})
              </li>
            ))}
          </ul>
        </section>
      )}
      {rows.length === 0 && <EmptyState emoji="📊">Noch keine Aufgaben. Lege im Board oder in der Liste Aufgaben an.</EmptyState>}
      <div className="gantt-chart">
        <ol className="gantt-labels" aria-label="Aufgaben">
          <li className="gantt-label-spacer" style={{ height: headerHeight }} aria-hidden="true" />
          <li className="gantt-label muted" style={{ height: milestoneRow }}>
            Meilensteine
          </li>
          {rows.map(({ task, depth }) => (
            <li key={task.id} className="gantt-label" style={{ height: rowHeight, paddingLeft: `${0.5 + depth}rem` }}>
              <button
                type="button"
                className={task.id === selected ? 'link-button selected' : 'link-button'}
                aria-pressed={task.id === selected}
                title={task.title}
                onClick={() => setSelected(task.id === selected ? null : task.id)}
              >
                {task.title}
              </button>
              {!spans.get(task.id) && <span className="muted"> · ohne Termin</span>}
            </li>
          ))}
        </ol>
        <div className="gantt-scroll" ref={scroller}>
          <svg
            className="gantt-svg"
            width={chartWidth}
            height={chartHeight}
            // A group, not an image: the bars inside are buttons, and an image hides its content from assistive technology.
            role="group"
            aria-label={`Zeitachse von ${displayDay(range.start)} bis ${displayDay(range.end)}`}
          >
            <defs>
              <marker id="gantt-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
                <path d="M0,0 L8,4 L0,8 z" className="gantt-arrowhead" />
              </marker>
              <marker id="gantt-arrow-violated" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
                <path d="M0,0 L8,4 L0,8 z" className="gantt-arrowhead violated" />
              </marker>
            </defs>
            {zoom !== 'month' &&
              Array.from({ length: range.end - range.start + 1 }, (_, i) => range.start + i)
                .filter(isWeekend)
                .map((day) => (
                  <rect key={day} className="gantt-weekend" x={x(day)} y={headerHeight} width={width} height={chartHeight - headerHeight} />
                ))}
            {header.top.map((segment) => (
              <g key={`top-${segment.start}`} className="gantt-header">
                <rect x={x(segment.start)} y={0} width={(segment.end - segment.start) * width} height={headerRow} />
                <text x={x(segment.start) + 4} y={headerRow - 7}>
                  {(segment.end - segment.start) * width > 60 ? segment.label : ''}
                </text>
              </g>
            ))}
            {header.bottom.map((segment) => (
              <g key={`bottom-${segment.start}`} className="gantt-header">
                <rect x={x(segment.start)} y={headerRow} width={(segment.end - segment.start) * width} height={headerRow} />
                <text x={x(segment.start) + ((segment.end - segment.start) * width) / 2} y={headerHeight - 7} textAnchor="middle">
                  {(segment.end - segment.start) * width >= 18 ? segment.label : ''}
                </text>
              </g>
            ))}
            {rows.map(({ task }, index) => (
              <line key={task.id} className="gantt-row-line" x1={0} x2={chartWidth} y1={rowTop(index + 1)} y2={rowTop(index + 1)} />
            ))}
            {now >= range.start && now <= range.end && (
              <line className="gantt-today" x1={x(now) + width / 2} x2={x(now) + width / 2} y1={headerHeight} y2={chartHeight}>
                <title>Heute</title>
              </line>
            )}
            {gantt.milestones.map((milestone) => {
              const center = x(parseDay(milestone.date)) + width / 2
              const middle = headerHeight + milestoneRow / 2
              return (
                <g key={milestone.id} className="gantt-milestone">
                  <title>{`${milestone.name}: ${displayDay(parseDay(milestone.date))}`}</title>
                  <line x1={center} x2={center} y1={headerHeight + milestoneRow} y2={chartHeight} />
                  <path d={`M${center},${middle - 7} L${center + 7},${middle} L${center},${middle + 7} L${center - 7},${middle} z`} />
                  <text x={center + 10} y={middle + 4}>
                    {milestone.name}
                  </text>
                </g>
              )
            })}
            {gantt.dependencies.map((dependency) => {
              const sourceIndex = rowIndex.get(dependency.sourceTaskId)
              const targetIndex = rowIndex.get(dependency.targetTaskId)
              const source = gantt.tasks.find((t) => t.id === dependency.sourceTaskId)
              const target = gantt.tasks.find((t) => t.id === dependency.targetTaskId)
              const sourceSpan = source && shownSpan(source)
              const targetSpan = target && shownSpan(target)
              if (sourceIndex === undefined || targetIndex === undefined || !sourceSpan || !targetSpan) return null
              const anchors = anchorsOf(dependency.dependencyType)
              const sx = anchors.source === 'end' ? x(sourceSpan.end + 1) : x(sourceSpan.start)
              const tx = anchors.target === 'end' ? x(targetSpan.end + 1) : x(targetSpan.start)
              const sy = rowTop(sourceIndex) + rowHeight / 2
              const ty = rowTop(targetIndex) + rowHeight / 2
              const out = sx + (anchors.source === 'end' ? 8 : -8)
              const into = tx + (anchors.target === 'start' ? -8 : 8)
              const between = ty > sy ? rowTop(targetIndex) : rowTop(targetIndex + 1)
              return (
                <path
                  key={dependency.id}
                  className={dependency.violated ? 'gantt-dependency violated' : 'gantt-dependency'}
                  d={`M${sx},${sy} H${out} V${between} H${into} V${ty} H${tx}`}
                  markerEnd={`url(#${dependency.violated ? 'gantt-arrow-violated' : 'gantt-arrow'})`}
                >
                  <title>{`${titles.get(dependency.sourceTaskId)} → ${titles.get(dependency.targetTaskId)} (${dependencyTypes[dependency.dependencyType]})`}</title>
                </path>
              )
            })}
            {rows.map(({ task }, index) => {
              const span = shownSpan(task)
              if (!span) return null
              const left = x(span.start)
              const barWidth = (span.end - span.start + 1) * width
              const top = rowTop(index) + (rowHeight - barHeight) / 2
              return (
                <g
                  key={task.id}
                  className={[
                    'gantt-bar',
                    `status-${task.status}`,
                    task.id === selected ? 'selected' : '',
                    drag?.taskId === task.id ? 'dragging' : '',
                    canContribute ? 'movable' : '',
                  ]
                    .filter(Boolean)
                    .join(' ')}
                  role="button"
                  tabIndex={0}
                  aria-label={`${task.title}: ${spanText(span)}, ${task.progress} % erledigt`}
                  data-task-id={task.id}
                  onPointerDown={(event) => pointerDown(event, task)}
                  onPointerMove={pointerMove}
                  onPointerUp={() => pointerUp(task)}
                  onPointerCancel={() => setDrag(null)}
                  onKeyDown={(event) => barKey(event, task)}
                >
                  <title>{`${task.title}: ${spanText(span)} · ${taskStatuses[task.status]}`}</title>
                  <rect className="gantt-bar-track" x={left} y={top} width={barWidth} height={barHeight} rx={4} />
                  <rect className="gantt-bar-progress" x={left} y={top} width={(barWidth * task.progress) / 100} height={barHeight} rx={4} />
                  {canContribute && (
                    <>
                      <rect className="gantt-handle" data-handle="start" x={left} y={top} width={handleWidth} height={barHeight} />
                      <rect className="gantt-handle" data-handle="end" x={left + barWidth - handleWidth} y={top} width={handleWidth} height={barHeight} />
                    </>
                  )}
                </g>
              )
            })}
          </svg>
        </div>
      </div>
      {selectedTask && (
        <TaskSchedule
          key={selectedTask.id}
          project={project}
          me={me}
          task={selectedTask}
          gantt={gantt}
          canContribute={canContribute}
          onReschedule={(span) => reschedule(selectedTask, span)}
          onLink={(sourceTaskId, type) => run(() => createDependency(project.id, sourceTaskId, selectedTask.id, type))}
          onUnlink={(dependency) => run(() => deleteDependency(dependency.id))}
          onChanged={() => {
            load()
            onChanged()
          }}
          onClose={() => setSelected(null)}
        />
      )}
      <Milestones
        milestones={gantt.milestones}
        canEdit={canEdit}
        onCreate={(name, date) => run(() => createMilestone(project.id, name, date))}
        onUpdate={(milestone, changes) => run(() => updateMilestone(milestone, changes))}
        onDelete={(milestone) => run(() => deleteMilestone(milestone.id))}
      />
    </section>
  )
}

type ScheduleProps = {
  project: ProjectDetails
  me: Me
  task: GanttTask
  gantt: Gantt
  canContribute: boolean
  onReschedule: (span: Span | null) => Promise<void>
  onLink: (sourceTaskId: string, type: DependencyType) => Promise<void>
  onUnlink: (dependency: GanttDependency) => Promise<void>
  onChanged: () => void
  onClose: () => void
}

/** Dates and dependencies of the selected task; the keyboard and screen reader way to plan. */
function TaskSchedule({ project, me, task, gantt, canContribute, onReschedule, onLink, onUnlink, onChanged, onClose }: ScheduleProps) {
  const [start, setStart] = useState(task.startDate ?? '')
  const [end, setEnd] = useState(task.dueDate ?? '')
  // Dates changed elsewhere (dragging, others) replace the form's; a chosen predecessor survives reloads.
  const [shownDates, setShownDates] = useState([task.startDate, task.dueDate])
  if (shownDates[0] !== task.startDate || shownDates[1] !== task.dueDate) {
    setShownDates([task.startDate, task.dueDate])
    setStart(task.startDate ?? '')
    setEnd(task.dueDate ?? '')
  }
  const [message, setMessage] = useState<string | null>(null)
  const [source, setSource] = useState('')
  const [type, setType] = useState<DependencyType>('finish_to_start')
  const [details, setDetails] = useState(false)
  const titles = new Map(gantt.tasks.map((t) => [t.id, t.title]))
  const predecessors = gantt.dependencies.filter((d) => d.targetTaskId === task.id)
  const successors = gantt.dependencies.filter((d) => d.sourceTaskId === task.id)
  const span = spanOf(task)

  function save(event: FormEvent) {
    event.preventDefault()
    setMessage(null)
    const startDay = start ? parseDay(start) : null
    const endDay = end ? parseDay(end) : null
    if (startDay !== null && endDay !== null && endDay < startDay) {
      setMessage('Das Ende darf nicht vor dem Start liegen.')
      return
    }
    const first = startDay ?? endDay
    const last = endDay ?? startDay
    void onReschedule(first === null || last === null ? null : { start: first, end: last })
  }

  function link(event: FormEvent) {
    event.preventDefault()
    if (source) void onLink(source, type)
  }

  const dependencyList = (label: string, dependencies: GanttDependency[], other: (d: GanttDependency) => string) => (
    <>
      <h5>{label}</h5>
      {dependencies.length === 0 ? (
        <p className="muted">Keine</p>
      ) : (
        <ul className="gantt-dependency-list">
          {dependencies.map((d) => (
            <li key={d.id} className={d.violated ? 'violated' : undefined}>
              {titles.get(other(d))} · {dependencyTypes[d.dependencyType]}
              {d.violated && <strong> · verletzt</strong>}
              {canContribute && (
                <button
                  type="button"
                  className="link-button"
                  aria-label={`Abhängigkeit zu „${titles.get(other(d))}“ entfernen`}
                  onClick={() => void onUnlink(d)}
                >
                  Entfernen
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  )

  return (
    <section className="gantt-details" aria-labelledby="gantt-task-heading">
      <header className="row">
        <h4 id="gantt-task-heading">{task.title}</h4>
        <button type="button" className="link-button" onClick={onClose}>
          Schließen
        </button>
      </header>
      <p className="muted">
        {taskStatuses[task.status]} · {task.progress} % erledigt
        {task.assigneeName && ` · ${task.assigneeName}`}
      </p>
      {canContribute ? (
        <form className="inline-form" onSubmit={save} aria-label="Termin">
          <label>
            Start
            <input type="date" value={start} onChange={(event) => setStart(event.target.value)} />
          </label>
          <label>
            Ende
            <input type="date" value={end} onChange={(event) => setEnd(event.target.value)} />
          </label>
          <button type="submit">Termin speichern</button>
          {message && <p role="alert">{message}</p>}
        </form>
      ) : (
        <p>Termin: {span ? spanText(span) : 'ohne Termin'}</p>
      )}
      {dependencyList('Vorgänger', predecessors, (d) => d.sourceTaskId)}
      {dependencyList('Nachfolger', successors, (d) => d.targetTaskId)}
      {canContribute && (
        <form className="inline-form" onSubmit={link} aria-label="Vorgänger hinzufügen">
          <label>
            Vorgänger
            <select value={source} onChange={(event) => setSource(event.target.value)} required>
              <option value="">Aufgabe wählen …</option>
              {gantt.tasks
                .filter((t) => t.id !== task.id)
                .map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.title}
                  </option>
                ))}
            </select>
          </label>
          <label>
            Art
            <select value={type} onChange={(event) => setType(event.target.value as DependencyType)}>
              {Object.entries(dependencyTypes).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <button type="submit">Vorgänger hinzufügen</button>
        </form>
      )}
      <button type="button" className="link-button" aria-expanded={details} onClick={() => setDetails(!details)}>
        {details ? 'Details ausblenden' : 'Details der Aufgabe anzeigen'}
      </button>
      {details && <TaskDetails taskId={task.id} project={project} me={me} onChanged={onChanged} onDeleted={() => {
        onClose()
        onChanged()
      }} onClose={() => setDetails(false)} />}
    </section>
  )
}

type MilestonesProps = {
  milestones: GanttMilestone[]
  canEdit: boolean
  onCreate: (name: string, date: string) => Promise<void>
  onUpdate: (milestone: GanttMilestone, changes: Partial<Pick<GanttMilestone, 'name' | 'date'>>) => Promise<void>
  onDelete: (milestone: GanttMilestone) => Promise<void>
}

function Milestones({ milestones, canEdit, onCreate, onUpdate, onDelete }: MilestonesProps) {
  const [name, setName] = useState('')
  const [date, setDate] = useState('')

  async function create(event: FormEvent) {
    event.preventDefault()
    await onCreate(name, date)
    setName('')
    setDate('')
  }

  return (
    <section className="gantt-milestones" aria-labelledby="milestones-heading">
      <h4 id="milestones-heading">Meilensteine</h4>
      {milestones.length === 0 ? (
        <p className="muted">Noch keine Meilensteine.</p>
      ) : (
        <ul>
          {milestones.map((milestone) => (
            <MilestoneItem
              key={`${milestone.id}-${milestone.version}`}
              milestone={milestone}
              canEdit={canEdit}
              onUpdate={(changes) => onUpdate(milestone, changes)}
              onDelete={() => {
                if (window.confirm(`Meilenstein „${milestone.name}“ löschen?`)) void onDelete(milestone)
              }}
            />
          ))}
        </ul>
      )}
      {canEdit && (
        <form className="inline-form" onSubmit={create} aria-label="Neuer Meilenstein">
          <label>
            Neuer Meilenstein
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
          </label>
          <label>
            Datum
            <input type="date" value={date} onChange={(event) => setDate(event.target.value)} required />
          </label>
          <button type="submit">Meilenstein anlegen</button>
        </form>
      )}
    </section>
  )
}

type MilestoneItemProps = {
  milestone: GanttMilestone
  canEdit: boolean
  onUpdate: (changes: Partial<Pick<GanttMilestone, 'name' | 'date'>>) => Promise<void>
  onDelete: () => void
}

function MilestoneItem({ milestone, canEdit, onUpdate, onDelete }: MilestoneItemProps) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(milestone.name)
  const [date, setDate] = useState(milestone.date)

  function save(event: FormEvent) {
    event.preventDefault()
    const changes = { ...(name !== milestone.name && { name }), ...(date !== milestone.date && { date }) }
    if (Object.keys(changes).length > 0) void onUpdate(changes)
    setEditing(false)
  }

  if (editing) {
    return (
      <li>
        <form className="inline-form" onSubmit={save} aria-label={`Meilenstein „${milestone.name}“ bearbeiten`}>
          <label>
            Name
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
          </label>
          <label>
            Datum
            <input type="date" value={date} onChange={(event) => setDate(event.target.value)} required />
          </label>
          <button type="submit">Speichern</button>
          <button type="button" onClick={() => setEditing(false)}>
            Abbrechen
          </button>
        </form>
      </li>
    )
  }

  return (
    <li className="gantt-milestone-item">
      <span>
        ◆ {milestone.name} · {displayDay(parseDay(milestone.date))}
      </span>
      <CalendarActions
        title={milestone.name}
        firstDay={milestone.date}
        lastDay={milestone.date}
        kind="Meilenstein"
        onDownload={() => downloadMilestoneCalendar(milestone.id, milestone.name)}
      />
      {canEdit && (
        <>
          <button type="button" className="link-button" aria-label={`Meilenstein „${milestone.name}“ bearbeiten`} onClick={() => setEditing(true)}>
            Bearbeiten
          </button>
          <button type="button" className="link-button" aria-label={`Meilenstein „${milestone.name}“ löschen`} onClick={onDelete}>
            Löschen
          </button>
        </>
      )}
    </li>
  )
}
