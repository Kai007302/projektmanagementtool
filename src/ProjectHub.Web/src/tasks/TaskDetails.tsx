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
import { NewTaskForm } from './TaskBoard'

type Props = {
  taskId: string
  project: ProjectDetails
  me: Me
  onChanged: () => void
  onDeleted: () => void
}

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

/** Roles that may work on tasks and therefore be assigned. */
const assignable = (member: ProjectMember) => ['admin', 'editor', 'member'].includes(member.role)

export function TaskDetails({ taskId, project, me, onChanged, onDeleted }: Props) {
  const [task, setTask] = useState<Task | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchTask(taskId).then(setTask, (e: Error) => setError(e.message))
  }, [taskId])

  useEffect(load, [load])

  async function remove() {
    if (!task || !window.confirm(`Aufgabe „${task.title}“ und ihre Unteraufgaben löschen?`)) return
    try {
      await deleteTask(task.id)
      onDeleted()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!task) return error ? <p role="alert">{error}</p> : <p>Aufgabe wird geladen …</p>

  const { canContribute, canEdit } = project.capabilities

  return (
    <article className="task-details" aria-labelledby="task-heading">
      <header className="project-header">
        <h4 id="task-heading">{task.title}</h4>
        {canEdit && (
          <button type="button" className="danger" onClick={remove}>
            Aufgabe löschen
          </button>
        )}
      </header>
      {error && <p role="alert">{error}</p>}
      {canContribute ? (
        <TaskForm
          key={task.version}
          task={task}
          members={project.members.filter(assignable)}
          onSaved={(saved) => {
            setError(null)
            setTask(saved)
            onChanged()
          }}
          onStale={() => {
            setError('Jemand anderes hat die Aufgabe inzwischen geändert. Der aktuelle Stand wurde geladen.')
            load()
            onChanged()
          }}
        />
      ) : (
        <p className="muted">
          {taskStatuses[task.status]} · {taskPriorities[task.priority]}
          {task.assigneeName && ` · ${task.assigneeName}`}
          {task.dueDate && ` · fällig ${task.dueDate}`}
        </p>
      )}
      <TaskCalendar task={task} />
      {canContribute && (
        <NewTaskForm
          label="Unteraufgabe"
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

type TaskFormProps = { task: Task; members: ProjectMember[]; onSaved: (task: Task) => void; onStale: () => void }

function TaskForm({ task, members, onSaved, onStale }: TaskFormProps) {
  const [title, setTitle] = useState(task.title)
  const [status, setStatus] = useState<TaskStatus>(task.status)
  const [priority, setPriority] = useState<TaskPriority>(task.priority)
  const [assigneeId, setAssigneeId] = useState(task.assigneeId ?? '')
  const [dueDate, setDueDate] = useState(task.dueDate ?? '')
  const [progress, setProgress] = useState(task.progress)
  const [message, setMessage] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setMessage(null)
    const edited: TaskChanges = {
      title,
      status,
      priority,
      assigneeId: assigneeId || null,
      dueDate: dueDate || null,
      progress,
    }
    // Send only what actually changed, so the activity feed and parallel edits stay precise.
    const changes = Object.fromEntries(
      Object.entries(edited).filter(([field, value]) => task[field as keyof TaskChanges] !== value),
    ) as TaskChanges
    if (Object.keys(changes).length === 0) return
    try {
      const saved = await updateTask(task.id, task.version, changes)
      if (changes.status === 'done') celebrate()
      onSaved(saved)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        onStale()
      } else {
        setMessage((e as Error).message)
      }
    }
  }

  return (
    <form className="stacked-form task-form" onSubmit={submit}>
      <label>
        Titel
        <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={500} />
      </label>
      <label>
        Status
        <select value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>
          {Object.entries(taskStatuses).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Priorität
        <select value={priority} onChange={(event) => setPriority(event.target.value as TaskPriority)}>
          {Object.entries(taskPriorities).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Zuständig
        <select value={assigneeId} onChange={(event) => setAssigneeId(event.target.value)}>
          <option value="">Niemand</option>
          {members.map((member) => (
            <option key={member.userId} value={member.userId}>
              {member.displayName}
            </option>
          ))}
        </select>
      </label>
      <label>
        Fällig am
        <input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} />
      </label>
      <label>
        Fortschritt (%)
        <input
          type="number"
          min={0}
          max={100}
          value={progress}
          onChange={(event) => setProgress(Number(event.target.value))}
        />
      </label>
      <button type="submit">Aufgabe speichern</button>
      {message && <p role="alert">{message}</p>}
    </form>
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
