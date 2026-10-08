import { apiFetch, jsonBody } from '../api/client'
import type { TaskPerson, TaskPriority, TaskStatus } from '../tasks/api'

export type KanbanCard = {
  id: string
  title: string
  status: TaskStatus
  priority: TaskPriority
  assignees: TaskPerson[]
  dueDate: string | null
  progress: number
  subtaskCount: number
  version: number
}

export type KanbanColumn = {
  id: string
  name: string
  taskStatus: TaskStatus
  wipLimit: number | null
  version: number
  cards: KanbanCard[]
}

export type KanbanBoard = { id: string; projectId: string; name: string; columns: KanbanColumn[] }

export type ColumnChanges = Partial<Pick<KanbanColumn, 'name' | 'taskStatus' | 'wipLimit'>>

export const fetchBoard = (projectId: string) => apiFetch<KanbanBoard>(`/api/v1/projects/${projectId}/board`)

export const moveCard = (card: KanbanCard, columnId: string, index: number) =>
  apiFetch<KanbanBoard>(`/api/v1/tasks/${card.id}/move`, {
    method: 'POST',
    body: jsonBody({ version: card.version, columnId, index }),
  })

export const createColumn = (projectId: string, name: string, taskStatus: TaskStatus) =>
  apiFetch<KanbanBoard>(`/api/v1/projects/${projectId}/board/columns`, { method: 'POST', body: jsonBody({ name, taskStatus }) })

export const updateColumn = (column: KanbanColumn, changes: ColumnChanges) =>
  apiFetch<KanbanBoard>(`/api/v1/board-columns/${column.id}`, {
    method: 'PATCH',
    body: jsonBody({ version: column.version, ...changes }),
  })

export const moveColumn = (column: KanbanColumn, index: number) =>
  apiFetch<KanbanBoard>(`/api/v1/board-columns/${column.id}/move`, { method: 'POST', body: jsonBody({ index }) })

export const deleteColumn = (column: KanbanColumn) => apiFetch<void>(`/api/v1/board-columns/${column.id}`, { method: 'DELETE' })
