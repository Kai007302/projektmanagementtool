import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import { GanttChart } from '../gantt/GanttChart'
import { KanbanBoard } from '../kanban/KanbanBoard'
import { ProjectKnowledge } from '../knowledge/ProjectKnowledge'
import { useProjectEvents } from '../realtime/projectEvents'
import { TaskBoard } from '../tasks/TaskBoard'
import { deleteProject, fetchProject, projectStatuses, updateProject, type ProjectDetails, type ProjectStatus } from './api'
import { ActivityFeed } from './ActivityFeed'
import { MembersPanel } from './MembersPanel'

type Props = { projectId: string; me: Me; onBack: () => void; onOpenArticle?: (id: string) => void; initialTaskId?: string | null }

type View = 'board' | 'list' | 'gantt'

const viewText: Record<View, string> = { board: 'Board', list: 'Liste', gantt: 'Gantt' }

export function ProjectView({ projectId, me, onBack, onOpenArticle, initialTaskId = null }: Props) {
  const [project, setProject] = useState<ProjectDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revision, setRevision] = useState(0)
  const [view, setView] = useState<View>('board')

  const load = useCallback(() => {
    fetchProject(projectId).then(setProject, (e: Error) => setError(e.message))
  }, [projectId])

  useEffect(load, [load])

  /** Something in the project changed: reload details and the activity feed. */
  const changed = useCallback(() => {
    load()
    setRevision((r) => r + 1)
  }, [load])

  // Changes by others arrive in realtime and reload what is shown.
  useProjectEvents(projectId, changed)

  async function remove() {
    if (!project || !window.confirm(`Projekt „${project.name}“ wirklich löschen?`)) return
    try {
      await deleteProject(project.id)
      onBack()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="project-view" aria-labelledby="project-heading">
      <button type="button" className="link-button" onClick={onBack}>
        ← Alle Projekte
      </button>
      {error && <p role="alert">{error}</p>}
      {!project ? (
        <p>Projekt wird geladen …</p>
      ) : (
        <>
          <header className="project-header">
            <div>
              <h2 id="project-heading">{project.name}</h2>
              <p className="muted">
                {projectStatuses[project.status]}
                {project.description && ` · ${project.description}`}
              </p>
            </div>
            {project.capabilities.canManage && (
              <button type="button" className="danger" onClick={remove}>
                Projekt löschen
              </button>
            )}
          </header>
          {project.capabilities.canEdit && <ProjectEditForm project={project} onSaved={changed} />}
          <nav className="tabs" aria-label="Ansicht">
            {(Object.keys(viewText) as View[]).map((value) => (
              <button
                key={value}
                type="button"
                className={value === view ? 'tab active' : 'tab'}
                aria-current={value === view ? 'page' : undefined}
                onClick={() => setView(value)}
              >
                {viewText[value]}
              </button>
            ))}
          </nav>
          <div className={view === 'list' ? 'project-layout' : 'project-layout wide'}>
            {view === 'board' && <KanbanBoard project={project} me={me} revision={revision} onChanged={changed} initialTaskId={initialTaskId} />}
            {view === 'list' && <TaskBoard project={project} me={me} revision={revision} onChanged={changed} />}
            {view === 'gantt' && <GanttChart project={project} me={me} revision={revision} onChanged={changed} />}
            <aside className="project-side">
              <MembersPanel project={project} onChanged={changed} />
              <ProjectKnowledge projectId={project.id} revision={revision} onOpenArticle={onOpenArticle} />
              <ActivityFeed projectId={project.id} revision={revision} />
            </aside>
          </div>
        </>
      )}
    </section>
  )
}

function ProjectEditForm({ project, onSaved }: { project: ProjectDetails; onSaved: () => void }) {
  const [name, setName] = useState(project.name)
  const [status, setStatus] = useState<ProjectStatus>(project.status)
  const [message, setMessage] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setMessage(null)
    try {
      await updateProject(project.id, project.version, { name, status })
      onSaved()
    } catch (e) {
      setMessage(
        e instanceof ApiError && e.status === 409
          ? 'Jemand anderes hat das Projekt inzwischen geändert. Der aktuelle Stand wurde geladen.'
          : (e as Error).message,
      )
      if (e instanceof ApiError && e.status === 409) onSaved()
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <label>
        Name
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
      </label>
      <label>
        Status
        <select value={status} onChange={(event) => setStatus(event.target.value as ProjectStatus)}>
          {Object.entries(projectStatuses).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <button type="submit">Speichern</button>
      {message && <p role="alert">{message}</p>}
    </form>
  )
}
