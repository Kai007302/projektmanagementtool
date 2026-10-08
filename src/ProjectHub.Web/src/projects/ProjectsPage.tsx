import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { initials } from '../identity/initials'
import type { Task } from '../tasks/api'
import { DueDate } from '../tasks/DueDate'
import { PriorityBadge } from '../tasks/PriorityBadge'
import { EmptyState } from '../ui/EmptyState'
import { Reveal } from '../ui/Reveal'
import { greeting, projectLook, today } from '../ui/personality'
import { canEditProject, createProject, fetchProjects, projectRoles, projectStatuses, type ProjectSummary } from './api'
import { ProjectIcon } from './ProjectIcon'
import { ProjectMenu } from './ProjectMenu'
import { ContinueSection } from './ContinueSection'
import { applyTemplate, projectTemplates, type ProjectTemplate } from './templates'
import { ProjectView } from './ProjectView'
import { useProjectOverview, type ProjectOverview } from './useProjectOverview'
import { toast } from '../ui/toast'
import { Skeleton } from '../ui/Skeleton'

type Props = {
  me: Me
  /** Only the projects of this department; '' for all I can see (ADR 0021). */
  departmentId?: string
  onOpenArticle?: (id: string) => void
  /** Opens this project (and task) right away, e.g. from a notification. */
  initialProjectId?: string | null
  initialTaskId?: string | null
}

export function ProjectsPage({ me, departmentId = '', onOpenArticle, initialProjectId = null, initialTaskId = null }: Props) {
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null)
  const [selected, setSelected] = useState<string | null>(initialProjectId)
  const [openBoard, setOpenBoard] = useState<string | null>(null)
  const [openTask, setOpenTask] = useState<{ projectId: string; taskId: string } | null>(
    initialProjectId && initialTaskId ? { projectId: initialProjectId, taskId: initialTaskId } : null,
  )
  const [error, setError] = useState<string | null>(null)
  const overview = useProjectOverview(projects)

  const load = useCallback(() => {
    fetchProjects(departmentId).then(
      (page) => setProjects(page.items),
      (e: Error) => setError(e.message),
    )
  }, [departmentId])

  useEffect(load, [load])

  if (selected) {
    return (
      <ProjectView
        key={selected}
        projectId={selected}
        me={me}
        initialTaskId={openTask?.projectId === selected ? openTask.taskId : null}
        initialView={openBoard ? 'whiteboard' : 'board'}
        initialBoardId={openBoard}
        onOpenArticle={onOpenArticle}
        onBack={() => {
          setSelected(null)
          setOpenTask(null)
          setOpenBoard(null)
          load()
        }}
      />
    )
  }

  return (
    <section aria-labelledby="projects-heading">
      <Greeting
        me={me}
        overview={overview}
        onOpen={(task) => {
          setOpenTask({ projectId: task.projectId, taskId: task.id })
          setSelected(task.projectId)
        }}
      />
      {projects && (
        <ContinueSection
          me={me}
          projects={projects}
          tasksOf={(projectId) => overview.get(projectId)?.tasks}
          onOpenArticle={onOpenArticle}
          onOpenProject={(projectId, boardId) => {
            setOpenBoard(boardId ?? null)
            setSelected(projectId)
          }}
        />
      )}
      <header className="page-header">
        <h2 id="projects-heading">Projekte</h2>
        <Reveal label="Projekt" title="Neues Projekt" primary>
          {() => (
            <CreateProjectForm
              departmentId={departmentId}
              onCreated={(project) => {
                load()
                setSelected(project.id)
              }}
            />
          )}
        </Reveal>
      </header>
      {error && <p role="alert">{error}</p>}
      {projects === null ? (
        <Skeleton kind="tiles" label="Projekte werden geladen" />
      ) : projects.length === 0 ? (
        <EmptyState emoji="🚀" hint="Leg mit „+ Projekt“ dein erstes Projekt an.">
          Du bist noch in keinem Projekt.
        </EmptyState>
      ) : (
        <ul className="project-grid" aria-labelledby="projects-heading">
          {projects.map((project) => (
            <li key={project.id} className="project-tile">
              <ProjectCard project={project} overview={overview.get(project.id)} onOpen={() => setSelected(project.id)} />
              <ProjectMenu className="project-tile-menu" project={project} canEdit={canEditProject(project.myRole)} onChanged={load} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function ProjectCard({ project, overview, onOpen }: { project: ProjectSummary; overview?: ProjectOverview; onOpen: () => void }) {
  const look = projectLook(project.id)
  const percent = overview && overview.total > 0 ? Math.round((overview.done / overview.total) * 100) : null
  const members = overview?.people ?? []
  return (
    <button type="button" className="project-card" style={{ ['--project' as string]: look.color }} onClick={onOpen}>
      <ProjectIcon project={project} className="project-card-icon" />
      <span className="project-card-body">
        <span className="card-title">{project.name}</span>
        <span className="card-meta">
          <span className={`project-status status-${project.status}`}>{projectStatuses[project.status]}</span>
          {project.myRole && ` · ${projectRoles[project.myRole]}`}
          {overview && ` · ${overview.done}/${overview.total} Aufgaben erledigt`}
        </span>
        {members.length > 0 && (
          <span className="avatar-stack" aria-hidden="true">
            {members.slice(0, 4).map((member) => (
              <span key={member.userId} className="avatar small" title={member.displayName}>
                {initials(member.displayName)}
              </span>
            ))}
            {members.length > 4 && <span className="avatar small more">+{members.length - 4}</span>}
          </span>
        )}
      </span>
      {percent !== null && <ProgressRing percent={percent} />}
    </button>
  )
}

/** Circular progress; the percentage is also part of the card text for screen readers. */
function ProgressRing({ percent }: { percent: number }) {
  const radius = 18
  const circumference = 2 * Math.PI * radius
  return (
    <span className="progress-ring" aria-hidden="true">
      <svg viewBox="0 0 44 44" width="44" height="44">
        <circle cx="22" cy="22" r={radius} className="progress-ring-track" />
        <circle
          cx="22"
          cy="22"
          r={radius}
          className="progress-ring-value"
          strokeDasharray={circumference}
          strokeDashoffset={circumference * (1 - percent / 100)}
          transform="rotate(-90 22 22)"
        />
      </svg>
      <span>{percent}%</span>
    </span>
  )
}

/** Personal start: greeting plus my open and overdue tasks across projects. */
function Greeting({ me, overview, onOpen }: { me: Me; overview: Map<string, ProjectOverview>; onOpen: (task: Task) => void }) {
  const day = today()
  const mine = [...overview.values()].flatMap((o) => o.tasks).filter((t) => t.assigneeId === me.id && t.status !== 'done')
  const overdue = mine.filter((t) => t.dueDate !== null && t.dueDate < day)
  const next = [...mine].sort((a, b) => (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999')).slice(0, 3)
  const firstName = me.displayName.split(/\s+/)[0]

  return (
    <section className="greeting" aria-labelledby="greeting-heading">
      <div>
        <h2 id="greeting-heading">
          {greeting()}, {firstName} <span aria-hidden="true">👋</span>
        </h2>
        <p className="muted">
          {mine.length === 0
            ? 'Keine offenen Aufgaben für dich. Genieß den Moment ☕'
            : `Du hast ${mine.length} offene ${mine.length === 1 ? 'Aufgabe' : 'Aufgaben'}${overdue.length > 0 ? `, ${overdue.length} davon überfällig` : ''}.`}
        </p>
      </div>
      <div className="greeting-stats">
        <span className="stat">
          <strong>{mine.length}</strong> offen
        </span>
        <span className={overdue.length > 0 ? 'stat alert' : 'stat'}>
          <strong>{overdue.length}</strong> überfällig
        </span>
      </div>
      {next.length > 0 && (
        <ul className="greeting-tasks">
          {next.map((task) => (
            <li key={task.id}>
              <button type="button" className="greeting-task" onClick={() => onOpen(task)}>
                <span className="greeting-task-title">{task.title}</span>
                <PriorityBadge priority={task.priority} />
                {task.dueDate && <DueDate date={task.dueDate} done={false} />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function CreateProjectForm({ departmentId, onCreated }: { departmentId: string; onCreated: (project: ProjectSummary) => void }) {
  const [name, setName] = useState('')
  const [template, setTemplate] = useState<ProjectTemplate['id']>('empty')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    let project: ProjectSummary
    try {
      project = await createProject(name, '', departmentId)
    } catch (e) {
      setError((e as Error).message)
      setBusy(false)
      return
    }
    try {
      await applyTemplate(project.id, projectTemplates.find((t) => t.id === template)!)
      toast(`Projekt „${project.name}“ angelegt. 🎉`)
    } catch {
      toast(`Projekt „${project.name}“ angelegt. Die Vorlage wurde nicht vollständig übernommen.`)
    }
    setName('')
    setBusy(false)
    onCreated(project)
  }

  return (
    <form className="stacked-form" onSubmit={submit}>
      <label>
        Name des Projekts
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} autoFocus />
      </label>
      <fieldset className="template-picker">
        <legend>Vorlage</legend>
        {projectTemplates.map((option) => (
          <label key={option.id} className="template-option">
            <input type="radio" name="template" value={option.id} checked={template === option.id} onChange={() => setTemplate(option.id)} />
            <span>
              <strong>{option.name}</strong>
              <small className="muted">{option.description}</small>
            </span>
          </label>
        ))}
      </fieldset>
      <button type="submit" disabled={busy}>
        {busy ? 'Wird angelegt …' : 'Projekt anlegen'}
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
