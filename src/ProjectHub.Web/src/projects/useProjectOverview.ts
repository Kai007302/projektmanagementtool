import { useEffect, useState } from 'react'
import { fetchTasks, type Task } from '../tasks/api'
import type { ProjectSummary } from './api'

export type ProjectPerson = { userId: string; displayName: string }

export type ProjectOverview = { total: number; done: number; people: ProjectPerson[]; tasks: Task[] }

/**
 * Projects whose tasks are loaded for the overview; beyond that the cards stay plain. One request per
 * project, so the start page stays well within the API rate limit.
 */
const LIMIT = 12

/** The people a project's tasks are assigned to, most tasks first. */
function peopleOf(tasks: Task[]): ProjectPerson[] {
  const counts = new Map<string, ProjectPerson & { count: number }>()
  for (const task of tasks) {
    if (!task.assigneeId) continue
    const entry = counts.get(task.assigneeId) ?? { userId: task.assigneeId, displayName: task.assigneeName ?? '?', count: 0 }
    entry.count++
    counts.set(task.assigneeId, entry)
  }
  return [...counts.values()].sort((a, b) => b.count - a.count).map(({ userId, displayName }) => ({ userId, displayName }))
}

/**
 * Loads the tasks of the listed projects for the start page (progress, team, my tasks).
 * Read-only and best effort: a project that fails to load simply shows no details.
 */
export function useProjectOverview(projects: ProjectSummary[] | null) {
  const [overview, setOverview] = useState<Map<string, ProjectOverview>>(new Map())

  useEffect(() => {
    if (!projects) return
    let current = true
    Promise.all(
      projects.slice(0, LIMIT).map((project) =>
        fetchTasks(project.id).then(
          ({ items }): [string, ProjectOverview] => [
            project.id,
            { total: items.length, done: items.filter((t) => t.status === 'done').length, people: peopleOf(items), tasks: items },
          ],
          () => null,
        ),
      ),
    ).then((entries) => current && setOverview(new Map(entries.filter((e): e is [string, ProjectOverview] => e !== null))))
    return () => {
      current = false
    }
  }, [projects])

  return overview
}
