import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { createTask, fetchTasks, taskStatuses, type Task } from './api'
import { PriorityBadge } from './PriorityBadge'
import { TaskDetails } from './TaskDetails'
import { useLatest } from '../api/useLatest'

type Props = { project: ProjectDetails; me: Me; revision: number; onChanged: () => void }

export function TaskBoard({ project, me, revision, onChanged }: Props) {
  const [tasks, setTasks] = useState<Task[] | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchTasks(project.id)).then(
      (page) => setTasks(page.items),
      (e: Error) => setError(e.message),
    )
  }, [latest, project.id])

  useEffect(load, [load, revision])

  const changed = useCallback(() => {
    load()
    onChanged()
  }, [load, onChanged])

  const childrenOf = useMemo(() => {
    const map = new Map<string | null, Task[]>()
    for (const task of tasks ?? []) {
      const siblings = map.get(task.parentTaskId) ?? []
      siblings.push(task)
      map.set(task.parentTaskId, siblings)
    }
    return map
  }, [tasks])

  const renderTasks = (parentId: string | null) => {
    const list = childrenOf.get(parentId)
    if (!list?.length) return null
    return (
      <ul className="task-list">
        {list.map((task) => (
          <li key={task.id}>
            <button
              type="button"
              className={`task-row${task.id === selected ? ' selected' : ''}`}
              onClick={() => setSelected(task.id === selected ? null : task.id)}
            >
              <span className={`task-status status-${task.status}`}>{taskStatuses[task.status]}</span>
              <span className="task-title">{task.title}</span>
              <PriorityBadge priority={task.priority} />
              <span className="card-meta">{[task.assigneeName, task.progress > 0 ? `${task.progress} %` : null].filter(Boolean).join(' · ')}</span>
            </button>
            {renderTasks(task.id)}
          </li>
        ))}
      </ul>
    )
  }

  return (
    <section className="panel task-board" aria-labelledby="tasks-heading">
      <h3 id="tasks-heading">Aufgaben</h3>
      {error && <p role="alert">{error}</p>}
      {project.capabilities.canContribute && (
        <NewTaskForm
          label="Neue Aufgabe"
          onCreate={async (title) => {
            await createTask(project.id, title, null)
            changed()
          }}
        />
      )}
      {tasks === null ? (
        <p>Aufgaben werden geladen …</p>
      ) : tasks.length === 0 ? (
        <p className="muted">Noch keine Aufgaben.</p>
      ) : (
        renderTasks(null)
      )}
      {selected && (
        <TaskDetails
          key={selected}
          taskId={selected}
          project={project}
          me={me}
          onChanged={changed}
          onDeleted={() => {
            setSelected(null)
            changed()
          }}
        />
      )}
    </section>
  )
}

export function NewTaskForm({ label, onCreate }: { label: string; onCreate: (title: string) => Promise<void> }) {
  const [title, setTitle] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await onCreate(title)
      setTitle('')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <label>
        {label}
        <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={500} />
      </label>
      <button type="submit">Anlegen</button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
