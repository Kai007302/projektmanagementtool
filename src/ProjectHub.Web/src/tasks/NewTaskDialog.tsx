import { useState, type FormEvent } from 'react'
import type { ProjectDetails, ProjectMember } from '../projects/api'
import { DateField } from '../ui/DateField'
import { Dialog } from '../ui/Dialog'
import { taskPriorities, taskStatuses, type NewTaskDetails, type TaskPriority, type TaskStatus } from './api'

type Props = {
  project: ProjectDetails
  /** Preset status, e.g. the status of the Kanban column the task is added to. */
  status?: TaskStatus
  /** Shown in the heading, e.g. the column name. */
  where?: string
  onCreate: (title: string, status: TaskStatus, details: NewTaskDetails) => Promise<void>
  onClose: () => void
}

const assignable = (member: ProjectMember) => ['admin', 'editor', 'member'].includes(member.role)

/** A window with everything a new task needs; only the title is required. */
export function NewTaskDialog({ project, status: initialStatus = 'todo', where, onCreate, onClose }: Props) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [status, setStatus] = useState<TaskStatus>(initialStatus)
  const [priority, setPriority] = useState<TaskPriority>('normal')
  const [assigneeIds, setAssigneeIds] = useState<string[]>([])
  const [startDate, setStartDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const members = project.members.filter(assignable)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const name = title.trim()
    if (!name) return
    if (startDate && dueDate && startDate > dueDate) {
      setError('Der Start liegt nach dem Fälligkeitsdatum.')
      return
    }
    setError(null)
    setSaving(true)
    try {
      await onCreate(name, status, {
        ...(description.trim() && { description: description.trim() }),
        priority,
        ...(assigneeIds.length > 0 && { assigneeIds }),
        ...(startDate && { startDate }),
        ...(dueDate && { dueDate }),
      })
      onClose()
    } catch (e) {
      setError((e as Error).message)
      setSaving(false)
    }
  }

  const toggle = (userId: string) => setAssigneeIds((ids) => (ids.includes(userId) ? ids.filter((id) => id !== userId) : [...ids, userId]))

  return (
    <Dialog label="Neue Aufgabe" title={where ? `Neue Aufgabe in ${where}` : 'Neue Aufgabe'} onClose={onClose}>
      <form className="stacked-form new-task" onSubmit={submit}>
        <label>
          Titel
          <input value={title} onChange={(event) => setTitle(event.target.value)} maxLength={500} required autoFocus />
        </label>
        <label>
          Beschreibung
          <textarea value={description} onChange={(event) => setDescription(event.target.value)} rows={3} maxLength={20000} />
        </label>
        <div className="new-task-row">
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
        </div>
        <div className="new-task-row">
          <label>
            Start
            <DateField value={startDate} onChange={setStartDate} />
          </label>
          <label>
            Fällig am
            <DateField value={dueDate} onChange={setDueDate} />
          </label>
        </div>
        {members.length > 0 && (
          <fieldset className="new-task-people">
            <legend>Zuständig</legend>
            {members.map((member) => (
              <label key={member.userId} className="check">
                <input type="checkbox" checked={assigneeIds.includes(member.userId)} onChange={() => toggle(member.userId)} />
                {member.displayName}
              </label>
            ))}
          </fieldset>
        )}
        {error && <p role="alert">{error}</p>}
        <div className="row">
          <button type="submit" disabled={saving || !title.trim()}>
            Aufgabe anlegen
          </button>
          <button type="button" onClick={onClose}>
            Abbrechen
          </button>
        </div>
      </form>
    </Dialog>
  )
}
