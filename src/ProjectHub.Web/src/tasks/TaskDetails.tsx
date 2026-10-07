import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import type { ProjectDetails, ProjectMember } from '../projects/api'
import { celebrate } from '../ui/confetti'
import { CalendarActions } from '../calendar/CalendarActions'
import { downloadTaskCalendar } from '../calendar/calendar'
import { fetchTaskWhiteboards, type TaskWhiteboard } from '../whiteboard/api'
import {
  addComment,
  createTask,
  deleteAttachment,
  deleteComment,
  deleteTask,
  downloadAttachment,
  fetchAttachments,
  fetchComments,
  fetchTask,
  taskPriorities,
  taskStatuses,
  updateTask,
  uploadAttachment,
  type Attachment,
  type Comment,
  type Task,
  type TaskChanges,
  type TaskPriority,
  type TaskStatus,
} from './api'
import { InlineEdit } from '../ui/InlineEdit'
import { Menu } from '../ui/Menu'
import { QuickCreate } from '../ui/QuickCreate'
import { Skeleton } from '../ui/Skeleton'

type Props = {
  taskId: string
  project: ProjectDetails
  me: Me
  onChanged: () => void
  onDeleted: () => void
  onClose: () => void
  /** Only shows the task; the Kanban board opens cards like this, changes happen in the list. */
  readOnly?: boolean
  /** Read-only view on the board: jumps to the same task in the list, where it can be edited. */
  onEditInList?: () => void
}

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

/** Roles that may work on tasks and therefore be assigned. */
const assignable = (member: ProjectMember) => ['admin', 'editor', 'member'].includes(member.role)

const staleText = 'Jemand anderes hat die Aufgabe inzwischen geändert. Der aktuelle Stand wurde geladen.'

/**
 * A task in a panel on the right, like in Asana: the board stays visible, every field saves as soon as it changes,
 * rare actions sit in the "…" menu. Escape or the close button closes it.
 */
export function TaskDetails({ taskId, project, me, onChanged, onDeleted, onClose, readOnly = false, onEditInList }: Props) {
  const [task, setTask] = useState<Task | null>(null)
  const [error, setError] = useState<string | null>(null)
  const current = useRef<Task | null>(null)
  const queue = useRef<Promise<void>>(Promise.resolve())
  const panel = useRef<HTMLElement>(null)

  const show = useCallback((next: Task) => {
    current.current = next
    setTask(next)
  }, [])

  const load = useCallback(() => {
    fetchTask(taskId).then(show, (e: Error) => setError(e.message))
  }, [taskId, show])

  useEffect(load, [load])
  useEffect(() => panel.current?.focus(), [])

  /** Saves changes one after another, so a quick second change uses the version the first one returned. */
  function save(changes: TaskChanges) {
    queue.current = queue.current.then(async () => {
      const before = current.current
      if (!before) return
      const changed = Object.fromEntries(Object.entries(changes).filter(([field, value]) => before[field as keyof TaskChanges] !== value)) as TaskChanges
      if (Object.keys(changed).length === 0) return
      setError(null)
      try {
        const saved = await updateTask(before.id, before.version, changed)
        if (changed.status === 'done') celebrate()
        show(saved)
        onChanged()
      } catch (e) {
        if (e instanceof ApiError && e.status === 409) {
          setError(staleText)
          load()
          onChanged()
        } else {
          setError((e as Error).message)
        }
      }
    })
    return queue.current
  }

  async function remove() {
    if (!task || !window.confirm(`Aufgabe „${task.title}“ und ihre Unteraufgaben löschen?`)) return
    try {
      await deleteTask(task.id)
      onDeleted()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const canContribute = project.capabilities.canContribute && !readOnly
  const canEdit = project.capabilities.canEdit && !readOnly

  return (
    <aside
      ref={panel}
      className="task-drawer"
      aria-labelledby="task-heading"
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && !event.defaultPrevented) onClose()
      }}
    >
      <header className="task-drawer-header">
        <span className="muted task-drawer-kicker">Aufgabe</span>
        {task && canEdit && <Menu label="Weitere Aktionen zur Aufgabe" items={[{ label: 'Aufgabe löschen', danger: true, onSelect: () => void remove() }]} />}
        <button type="button" className="icon-button" aria-label="Aufgabe schließen" title="Schließen (Esc)" onClick={onClose}>
          ✕
        </button>
      </header>
      {error && <p role="alert">{error}</p>}
      {!task ? (
        !error && <Skeleton count={6} label="Aufgabe wird geladen" />
      ) : (
        <article className="task-details">
          <h4 id="task-heading" className="task-drawer-title">
            <InlineEdit key={task.title} value={task.title} label="Titel" maxLength={500} editable={canContribute} onSave={(title) => save({ title })} />
          </h4>
          <TaskFields key={task.version} task={task} members={project.members.filter(assignable)} editable={canContribute} onSave={save} />
          {readOnly && project.capabilities.canContribute && (
            <p className="read-only-hint">
              <span className="muted">Auf dem Board nur zum Ansehen.</span>
              {onEditInList && (
                <button type="button" className="primary-button" onClick={onEditInList}>
                  In der Liste bearbeiten
                </button>
              )}
            </p>
          )}
          <TaskCalendar task={task} />
          {canContribute && (
            <QuickCreate
              label="Unteraufgabe"
              fieldLabel="Neue Unteraufgabe"
              onCreate={async (title) => {
                await createTask(project.id, title, task.id)
                onChanged()
              }}
            />
          )}
          <CommentsPanel taskId={task.id} project={project} me={me} onChanged={onChanged} />
          <AttachmentsPanel taskId={task.id} project={project} me={me} onChanged={onChanged} />
          <TaskWhiteboards taskId={task.id} />
        </article>
      )}
    </aside>
  )
}

/** Puts a task with a start or due date into the person's own calendar (DEC-028). */
function TaskCalendar({ task }: { task: Task }) {
  const firstDay = task.startDate ?? task.dueDate
  const lastDay = task.dueDate ?? task.startDate
  if (!firstDay || !lastDay) return null
  return (
    <p className="muted">
      Kalender: <CalendarActions title={task.title} firstDay={firstDay} lastDay={lastDay} kind="Aufgabe" onDownload={() => downloadTaskCalendar(task.id, task.title)} />
    </p>
  )
}

/** Whiteboards that show this task as a card (updated a few seconds after a board changes). */
function TaskWhiteboards({ taskId }: { taskId: string }) {
  const [boards, setBoards] = useState<TaskWhiteboard[]>([])
  useEffect(() => {
    fetchTaskWhiteboards(taskId).then(setBoards, () => setBoards([]))
  }, [taskId])

  if (boards.length === 0) return null
  return (
    <p className="muted">
      Auf Whiteboards: {boards.map((board) => board.name).join(', ')}
    </p>
  )
}

type FieldsProps = { task: Task; members: ProjectMember[]; editable: boolean; onSave: (changes: TaskChanges) => Promise<void> }

/** The task's properties as a compact list; each one saves on its own, there is no save button. */
function TaskFields({ task, members, editable, onSave }: FieldsProps) {
  const [dueDate, setDueDate] = useState(task.dueDate ?? '')
  const [progress, setProgress] = useState(String(task.progress))

  if (!editable) {
    return (
      <dl className="task-props">
        <dt>Status</dt>
        <dd>{taskStatuses[task.status]}</dd>
        <dt>Priorität</dt>
        <dd>{taskPriorities[task.priority]}</dd>
        <dt>Zuständig</dt>
        <dd>{task.assigneeName ?? 'Niemand'}</dd>
        {task.dueDate && (
          <>
            <dt>Fällig am</dt>
            <dd>{task.dueDate}</dd>
          </>
        )}
      </dl>
    )
  }

  const saveProgress = () => {
    const value = Math.min(100, Math.max(0, Math.round(Number(progress))))
    if (Number.isFinite(value)) void onSave({ progress: value })
  }

  return (
    <div className="task-props">
      <label htmlFor="task-status">Status</label>
      <select id="task-status" value={task.status} onChange={(event) => void onSave({ status: event.target.value as TaskStatus })}>
        {Object.entries(taskStatuses).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <label htmlFor="task-priority">Priorität</label>
      <select id="task-priority" value={task.priority} onChange={(event) => void onSave({ priority: event.target.value as TaskPriority })}>
        {Object.entries(taskPriorities).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <label htmlFor="task-assignee">Zuständig</label>
      <select id="task-assignee" value={task.assigneeId ?? ''} onChange={(event) => void onSave({ assigneeId: event.target.value || null })}>
        <option value="">Niemand</option>
        {members.map((member) => (
          <option key={member.userId} value={member.userId}>
            {member.displayName}
          </option>
        ))}
      </select>
      <label htmlFor="task-due">Fällig am</label>
      <input id="task-due" type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} onBlur={() => void onSave({ dueDate: dueDate || null })} />
      <label htmlFor="task-progress">Fortschritt (%)</label>
      <input
        id="task-progress"
        type="number"
        min={0}
        max={100}
        value={progress}
        onChange={(event) => setProgress(event.target.value)}
        onBlur={saveProgress}
        onKeyDown={(event) => {
          if (event.key === 'Enter') saveProgress()
        }}
      />
    </div>
  )
}

type PanelProps = { taskId: string; project: ProjectDetails; me: Me; onChanged: () => void }

/** Finds "@Display Name" of project members in a comment. */
function mentionedIn(content: string, members: ProjectMember[]): string[] {
  return members.filter((member) => content.includes(`@${member.displayName}`)).map((member) => member.userId)
}

function CommentsPanel({ taskId, project, me, onChanged }: PanelProps) {
  const [comments, setComments] = useState<Comment[]>([])
  const [content, setContent] = useState('')
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchComments(taskId).then((page) => setComments(page.items), (e: Error) => setError(e.message))
  }, [taskId])

  useEffect(load, [load])

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
      load()
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    void run(async () => {
      await addComment(taskId, content, mentionedIn(content, project.members))
      setContent('')
    })
  }

  return (
    <section aria-labelledby="comments-heading">
      <h4 id="comments-heading">Kommentare</h4>
      {error && <p role="alert">{error}</p>}
      <ul className="plain-list">
        {comments.map((comment) => (
          <li key={comment.id} className="comment">
            <div className="row">
              <span>
                <strong>{comment.authorName}</strong>{' '}
                <small className="muted">{dateFormat.format(new Date(comment.createdAt))}</small>
              </span>
              {(comment.authorId === me.id || project.capabilities.canManage) && (
                <button
                  type="button"
                  className="link-button"
                  aria-label={`Kommentar von ${comment.authorName} löschen`}
                  onClick={() => run(() => deleteComment(comment.id))}
                >
                  Löschen
                </button>
              )}
            </div>
            <p>{comment.content}</p>
          </li>
        ))}
      </ul>
      {project.capabilities.canContribute && (
        <form className="stacked-form" onSubmit={submit}>
          <label>
            Kommentar
            <textarea
              value={content}
              onChange={(event) => setContent(event.target.value)}
              required
              maxLength={10000}
              rows={3}
              placeholder="@Name erwähnt Projektmitglieder"
            />
          </label>
          <button type="submit">Kommentieren</button>
        </form>
      )}
    </section>
  )
}

function AttachmentsPanel({ taskId, project, me, onChanged }: PanelProps) {
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [error, setError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const load = useCallback(() => {
    fetchAttachments(taskId).then(setAttachments, (e: Error) => setError(e.message))
  }, [taskId])

  useEffect(load, [load])

  async function run(action: () => Promise<unknown>, reload = true) {
    setError(null)
    try {
      await action()
      if (reload) {
        load()
        onChanged()
      }
    } catch (e) {
      setError((e as Error).message)
    }
  }

  function upload(event: FormEvent) {
    event.preventDefault()
    const file = fileInput.current?.files?.[0]
    if (!file) return
    void run(async () => {
      await uploadAttachment(taskId, file)
      if (fileInput.current) fileInput.current.value = ''
    })
  }

  return (
    <section aria-labelledby="attachments-heading">
      <h4 id="attachments-heading">Dateien</h4>
      {error && <p role="alert">{error}</p>}
      <ul className="plain-list">
        {attachments.map((attachment) => (
          <li key={attachment.id} className="row">
            <button
              type="button"
              className="link-button"
              onClick={() => run(() => downloadAttachment(attachment), false)}
            >
              {attachment.fileName}
            </button>
            <span className="row">
              <small className="muted">
                {formatSize(attachment.sizeBytes)} · {attachment.uploadedByName}
              </small>
              {(attachment.uploadedBy === me.id || project.capabilities.canManage) && (
                <button
                  type="button"
                  aria-label={`${attachment.fileName} löschen`}
                  onClick={() => run(() => deleteAttachment(attachment.id))}
                >
                  ✕
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>
      {project.capabilities.canContribute && (
        <form className="inline-form" onSubmit={upload}>
          <label>
            Datei
            <input type="file" ref={fileInput} />
          </label>
          <button type="submit">Hochladen</button>
        </form>
      )}
    </section>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
