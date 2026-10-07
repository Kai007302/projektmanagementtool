import { useEffect, useState } from 'react'
import { fetchGantt, type GanttMilestone } from '../gantt/api'
import type { Me } from '../identity/api'
import type { Task } from '../tasks/api'
import { today } from '../ui/personality'
import { recentVisits, type Visit } from '../ui/recent'
import type { ProjectSummary } from './api'

const kindText: Record<Visit['kind'], string> = { project: 'Projekt', article: 'Artikel', whiteboard: 'Whiteboard' }
const kindIcon: Record<Visit['kind'], string> = { project: '📁', article: '📄', whiteboard: '🖍️' }
const dateFormat = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'long' })

type Props = {
  me: Me
  projects: ProjectSummary[]
  /** Tasks of the projects on the start page, for the progress towards the next milestone. */
  tasksOf: (projectId: string) => Task[] | undefined
  onOpenProject: (projectId: string, boardId?: string) => void
  onOpenArticle?: (id: string) => void
}

/**
 * "Weiter, wo du warst": the last opened project, article and whiteboard, and how far the last project is from its
 * next milestone. Unfinished things pull people back (Zeigarnik) and a visible goal motivates (goal gradient).
 */
export function ContinueSection({ me, projects, tasksOf, onOpenProject, onOpenArticle }: Props) {
  const [visits] = useState(() => recentVisits(me.id))
  const known = new Set(projects.map((p) => p.id))
  const shown = visits
    .filter((v) => (v.kind === 'project' ? known.has(v.id) : v.kind === 'whiteboard' ? known.has(v.projectId) : Boolean(onOpenArticle)))
    .slice(0, 3)
  const lastProject = shown.find((v) => v.kind === 'project')?.id ?? null
  const milestone = useNextMilestone(lastProject)

  if (shown.length === 0) return null

  const tasks = lastProject ? tasksOf(lastProject) : undefined
  const due = milestone && tasks ? tasks.filter((t) => t.parentTaskId === null && t.dueDate !== null && t.dueDate <= milestone.date) : []
  const done = due.filter((t) => t.status === 'done').length

  function open(visit: Visit) {
    if (visit.kind === 'project') onOpenProject(visit.id)
    else if (visit.kind === 'whiteboard') onOpenProject(visit.projectId, visit.id)
    else onOpenArticle?.(visit.id)
  }

  return (
    <section className="continue" aria-labelledby="continue-heading">
      <h3 id="continue-heading">Weiter, wo du warst</h3>
      <ul className="continue-list">
        {shown.map((visit) => (
          <li key={`${visit.kind}:${visit.id}`}>
            <button type="button" className="continue-item" onClick={() => open(visit)}>
              <span className="continue-icon" aria-hidden="true">
                {kindIcon[visit.kind]}
              </span>
              <span className="continue-text">
                <span className="continue-title">{visit.title}</span>
                <span className="muted continue-kind">{kindText[visit.kind]}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {milestone && due.length > 0 && (
        <p className="continue-milestone">
          <span>
            Nächster Meilenstein <strong>{milestone.name}</strong> am {dateFormat.format(new Date(`${milestone.date}T00:00:00`))}: {done} von {due.length}{' '}
            Aufgaben erledigt
          </span>
          <span className="meter" aria-hidden="true">
            <span style={{ width: `${Math.round((done / due.length) * 100)}%` }} />
          </span>
        </p>
      )}
    </section>
  )
}

/** The next milestone (today or later) of a project; best effort, nothing on errors. */
function useNextMilestone(projectId: string | null) {
  const [milestone, setMilestone] = useState<GanttMilestone | null>(null)
  useEffect(() => {
    if (!projectId) return
    let current = true
    const day = today()
    fetchGantt(projectId).then(
      (gantt) => {
        const next = gantt.milestones.filter((m) => m.date >= day).sort((a, b) => a.date.localeCompare(b.date))[0]
        if (current) setMilestone(next ?? null)
      },
      () => {},
    )
    return () => {
      current = false
    }
  }, [projectId])
  return milestone
}
