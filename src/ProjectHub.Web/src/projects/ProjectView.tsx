import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import { GanttChart } from '../gantt/GanttChart'
import { KanbanBoard } from '../kanban/KanbanBoard'
import { ProjectKnowledge } from '../knowledge/ProjectKnowledge'
import { useProjectEvents } from '../realtime/projectEvents'
import { TaskBoard } from '../tasks/TaskBoard'
import { WhiteboardPanel } from '../whiteboard/WhiteboardPanel'
import { deleteProject, fetchProject, projectStatuses, updateProject, type ProjectDetails, type ProjectStatus } from './api'
import { ActivityFeed } from './ActivityFeed'
import { MembersPanel } from './MembersPanel'
import { WebexPanel } from '../webex/WebexPanel'
import { useLatest } from '../api/useLatest'
import { InlineEdit } from '../ui/InlineEdit'
import { projectLook } from '../ui/personality'
import { ProjectIcon } from './ProjectIcon'
import { ProjectMenu } from './ProjectMenu'

type Props = { projectId: string; me: Me; onBack: () => void; onOpenArticle?: (id: string) => void; initialTaskId?: string | null }

type View = 'board' | 'list' | 'gantt' | 'whiteboard'

const viewText: Record<View, string> = { board: 'Board', list: 'Liste', gantt: 'Gantt', whiteboard: 'Whiteboard' }

export function ProjectView({ projectId, me, onBack, onOpenArticle, initialTaskId = null }: Props) {
  const [project, setProject] = useState<ProjectDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revision, setRevision] = useState(0)
  const [view, setView] = useState<View>('board')

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchProject(projectId)).then(setProject, (e: Error) => setError(e.message))
  }, [latest, projectId])

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

  /** Saves one field right away; a conflict loads the current state and says so. */
  async function save(changes: { name?: string; status?: ProjectStatus }) {
    if (!project) return
    setError(null)
    try {
      await updateProject(project.id, project.version, changes)
    } catch (e) {
      setError(
        e instanceof ApiError && e.status === 409
          ? 'Jemand anderes hat das Projekt inzwischen geändert. Der aktuelle Stand wurde geladen.'
          : (e as Error).message,
      )
    }
    changed()
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
              <h2 id="project-heading" className="project-title" style={{ ['--project' as string]: projectLook(project.id).color }}>
                <ProjectIcon project={project} className="project-card-icon project-title-icon" />
                <InlineEdit key={project.name} value={project.name} label="Projektname" editable={project.capabilities.canEdit} onSave={(name) => save({ name })} />
              </h2>
              <p className="muted project-subline">
                {project.capabilities.canEdit ? (
                  <select
                    className={`status-chip project-status-${project.status}`}
                    aria-label="Projektstatus"
                    value={project.status}
                    onChange={(event) => void save({ status: event.target.value as ProjectStatus })}
                  >
                    {Object.entries(projectStatuses).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                ) : (
                  projectStatuses[project.status]
                )}
                {project.description && <span>{project.description}</span>}
              </p>
            </div>
            <ProjectMenu
              project={project}
              canEdit={project.capabilities.canEdit}
              extraItems={project.capabilities.canManage ? [{ label: 'Projekt löschen', danger: true, onSelect: () => void remove() }] : []}
              onChanged={changed}
            />
          </header>
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
            {view === 'whiteboard' && <WhiteboardPanel project={project} me={me} revision={revision} onChanged={changed} />}
            <aside className="project-side">
              <MembersPanel project={project} onChanged={changed} />
              <WebexPanel project={project} revision={revision} onChanged={changed} />
              <ProjectKnowledge projectId={project.id} revision={revision} onOpenArticle={onOpenArticle} />
              <ActivityFeed projectId={project.id} revision={revision} />
            </aside>
          </div>
        </>
      )}
    </section>
  )
}
