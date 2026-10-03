import { apiFetch, jsonBody } from '../api/client'
import type { TaskStatus } from '../tasks/api'

export type DependencyType = 'finish_to_start' | 'start_to_start' | 'finish_to_finish' | 'start_to_finish'

export type GanttTask = {
  id: string
  parentTaskId: string | null
  title: string
  status: TaskStatus
  assigneeName: string | null
  startDate: string | null
  dueDate: string | null
  progress: number
  version: number
}

export type GanttDependency = {
  id: string
  sourceTaskId: string
  targetTaskId: string
  dependencyType: DependencyType
  violated: boolean
}

export type GanttMilestone = { id: string; name: string; date: string; version: number }

export type Gantt = { projectId: string; tasks: GanttTask[]; dependencies: GanttDependency[]; milestones: GanttMilestone[] }

export const dependencyTypes: Record<DependencyType, string> = {
  finish_to_start: 'Ende → Anfang',
  start_to_start: 'Anfang → Anfang',
  finish_to_finish: 'Ende → Ende',
  start_to_finish: 'Anfang → Ende',
}

export const fetchGantt = (projectId: string) => apiFetch<Gantt>(`/api/v1/projects/${projectId}/gantt`)

export const createDependency = (projectId: string, sourceTaskId: string, targetTaskId: string, dependencyType: DependencyType) =>
  apiFetch<GanttDependency>(`/api/v1/projects/${projectId}/gantt/dependencies`, {
    method: 'POST',
    body: jsonBody({ sourceTaskId, targetTaskId, dependencyType }),
  })

export const deleteDependency = (id: string) => apiFetch<void>(`/api/v1/task-dependencies/${id}`, { method: 'DELETE' })

export const createMilestone = (projectId: string, name: string, date: string) =>
  apiFetch<GanttMilestone>(`/api/v1/projects/${projectId}/gantt/milestones`, { method: 'POST', body: jsonBody({ name, date }) })

export const updateMilestone = (milestone: GanttMilestone, changes: Partial<Pick<GanttMilestone, 'name' | 'date'>>) =>
  apiFetch<GanttMilestone>(`/api/v1/gantt-milestones/${milestone.id}`, {
    method: 'PATCH',
    body: jsonBody({ version: milestone.version, ...changes }),
  })

export const deleteMilestone = (id: string) => apiFetch<void>(`/api/v1/gantt-milestones/${id}`, { method: 'DELETE' })
