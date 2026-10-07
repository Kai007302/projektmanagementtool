import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import { GanttChart } from '../gantt/GanttChart'
import { KanbanBoard } from '../kanban/KanbanBoard'
import { ProjectKnowledge } from '../knowledge/ProjectKnowledge'
import { useProjectEvents } from '../realtime/projectEvents'
import { TaskBoard } from '../tasks/TaskBoard'
import { WhiteboardPanel } from '../whiteboard/WhiteboardPanel'
import { deleteProject, fetchProject, projectStatuses, updateProject, type ProjectDetails, type ProjectMember, type ProjectStatus } from './api'
import { ActivityFeed } from './ActivityFeed'
import { MembersPanel } from './MembersPanel'
import { WebexPanel } from '../webex/WebexPanel'
import { fetchProjectWebex } from '../webex/api'
import { Dialog } from '../ui/Dialog'
import { initials } from '../identity/initials'
import { useLatest } from '../api/useLatest'
import { InlineEdit } from '../ui/InlineEdit'
import { projectLook } from '../ui/personality'
import { ProjectIcon } from './ProjectIcon'
import { ProjectMenu } from './ProjectMenu'
import { Skeleton } from '../ui/Skeleton'
import { rememberVisit } from '../ui/recent'

export type View = 'board' | 'list' | 'gantt' | 'whiteboard' | 'knowledge' | 'activity' | 'webex'

type Props = {
  projectId: string
  me: Me
  onBack: () => void
  onOpenArticle?: (id: string) => void
  initialTaskId?: string | null
  initialView?: View
  /** Whiteboard to show first in the whiteboard view, e.g. from "Weiter, wo du warst". */
  initialBoardId?: string | null
}

const viewText: Record<View, string> = {
  board: 'Board',
  list: 'Liste',
  gantt: 'Gantt',
  whiteboard: 'Whiteboard',
  knowledge: 'Wissen',
  activity: 'Aktivität',
  webex: 'Webex',
}

export function ProjectView({ projectId, me, onBack, onOpenArticle, initialTaskId = null, initialView = 'board', initialBoardId = null }: Props) {
  const [project, setProject] = useState<ProjectDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revision, setRevision] = useState(0)
  const [view, setView] = useState<View>(initialView)
  const [membersOpen, setMembersOpen] = useState(false)
  const webexShown = useWebexShown(projectId, revision)
  const views = (Object.keys(viewText) as View[]).filter((value) => value !== 'webex' || webexShown)

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchProject(projectId)).then(setProject, (e: Error) => setError(e.message))
  }, [latest, projectId])

  useEffect(load, [load])

  const name = project?.name
  useEffect(() => {
    if (name) rememberVisit(me.id, { kind: 'project', id: projectId, title: name })
  }, [me.id, projectId, name])

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
        <Skeleton kind="board" label="Projekt wird geladen" />
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
            <div className="project-header-actions">
              <MembersButton members={project.members} onOpen={() => setMembersOpen(true)} />
              <ProjectMenu
                project={project}
                canEdit={project.capabilities.canEdit}
                extraItems={project.capabilities.canManage ? [{ label: 'Projekt löschen', danger: true, onSelect: () => void remove() }] : []}
                onChanged={changed}
              />
            </div>
          </header>
          {membersOpen && (
            <Dialog label="Mitglieder" onClose={() => setMembersOpen(false)}>
              <MembersPanel project={project} onChanged={changed} />
            </Dialog>
          )}
          <nav className="tabs" aria-label="Ansicht">
            {views.map((value) => (
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
          <div className="project-content">
            {view === 'board' && (
              <KanbanBoard
                project={project}
                me={me}
                revision={revision}
                onChanged={changed}
                initialTaskId={initialTaskId}
              />
            )}
            {view === 'list' && <TaskBoard project={project} me={me} revision={revision} onChanged={changed} />}
            {view === 'gantt' && <GanttChart project={project} me={me} revision={revision} onChanged={changed} />}
            {view === 'whiteboard' && <WhiteboardPanel project={project} me={me} revision={revision} onChanged={changed} initialBoardId={initialBoardId} />}
            {view === 'knowledge' && <ProjectKnowledge projectId={project.id} revision={revision} onOpenArticle={onOpenArticle} />}
            {view === 'activity' && <ActivityFeed projectId={project.id} revision={revision} />}
            {view === 'webex' && <WebexPanel project={project} revision={revision} onChanged={changed} />}
          </div>
        </>
      )}
    </section>
  )
}

/** The team as a row of avatars in the project header; opens the member list (add, change roles, remove). */
function MembersButton({ members, onOpen }: { members: ProjectMember[]; onOpen: () => void }) {
  return (
    <button type="button" className="members-button" aria-haspopup="dialog" title="Mitglieder verwalten" onClick={onOpen}>
      <span className="avatar-stack" aria-hidden="true">
        {members.slice(0, 4).map((member) => (
          <span key={member.userId} className="avatar small">
            {initials(member.displayName)}
          </span>
        ))}
        {members.length > 4 && <span className="avatar small more">+{members.length - 4}</span>}
      </span>
      <span>{members.length === 1 ? '1 Mitglied' : `${members.length} Mitglieder`}</span>
    </button>
  )
}

/** Whether the project has a Webex tab: Webex is set up, or links exist from before. */
function useWebexShown(projectId: string, revision: number) {
  const [shown, setShown] = useState(false)
  useEffect(() => {
    let current = true
    fetchProjectWebex(projectId).then(
      (webex) => current && setShown(webex.available || webex.links.length > 0),
      () => {},
    )
    return () => {
      current = false
    }
  }, [projectId, revision])
  return shown
}
