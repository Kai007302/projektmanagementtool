import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { initials } from '../identity/initials'
import type { Task } from '../tasks/api'
import { DueDate } from '../tasks/DueDate'
import { PriorityBadge } from '../tasks/PriorityBadge'
import { EmptyState } from '../ui/EmptyState'
import { greeting, projectLook, today } from '../ui/personality'
import { createProject, fetchProjects, projectRoles, projectStatuses, type ProjectSummary } from './api'
import { ProjectView } from './ProjectView'
import { useProjectOverview, type ProjectOverview } from './useProjectOverview'

type Props = {
  me: Me
  onOpenArticle?: (id: string) => void
  /** Opens this project (and task) right away, e.g. from a notification. */
  initialProjectId?: string | null
  initialTaskId?: string | null
}

export function ProjectsPage({ me, onOpenArticle, initialProjectId = null, initialTaskId = null }: Props) {
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null)
  const [selected, setSelected] = useState<string | null>(initialProjectId)
  const [openTask, setOpenTask] = useState<{ projectId: string; taskId: string } | null>(
    initialProjectId && initialTaskId ? { projectId: initialProjectId, taskId: initialTaskId } : null,
  )
  const [error, setError] = useState<string | null>(null)
  const overview = useProjectOverview(projects)

  const load = useCallback(() => {
    fetchProjects().then(
      (page) => setProjects(page.items),
      (e: Error) => setError(e.message),
    )
  }, [])

  useEffect(load, [load])

  if (selected) {
    return (
      <ProjectView
        key={selected}
        projectId={selected}
        me={me}
        initialTaskId={openTask?.projectId === selected ? openTask.taskId : null}
        onOpenArticle={onOpenArticle}
        onBack={() => {
          setSelected(null)
          setOpenTask(null)
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
      <h2 id="projects-heading">Projekte</h2>
      {error && <p role="alert">{error}</p>}
      <CreateProjectForm
        onCreated={(project) => {
          load()
          setSelected(project.id)
        }}
      />
      {projects === null ? (
        <p>Projekte werden geladen …</p>
      ) : projects.length === 0 ? (
        <EmptyState emoji="🚀" hint="Leg oben dein erstes Projekt an.">
          Du bist noch in keinem Projekt.
        </EmptyState>
      ) : (
        <ul className="project-grid">
          {projects.map((project) => (
            <li key={project.id}>
              <ProjectCard project={project} overview={overview.get(project.id)} onOpen={() => setSelected(project.id)} />
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
      <span className="project-card-icon" aria-hidden="true">
        {look.emoji}
      </span>
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

function CreateProjectForm({ onCreated }: { onCreated: (project: ProjectSummary) => void }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const project = await createProject(name, '')
      setName('')
      onCreated(project)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <label>
        Neues Projekt
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
      </label>
      <button type="submit">Projekt anlegen</button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
