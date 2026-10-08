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
  color: ColumnColor | null
  version: number
  cards: KanbanCard[]
}

export type KanbanBoard = { id: string; projectId: string; name: string; columns: KanbanColumn[] }

/** The palette the API accepts for a column (migration 018). */
export const columnColors = {
  gray: { label: 'Grau', value: '#94a3b8' },
  blue: { label: 'Blau', value: '#3b82f6' },
  green: { label: 'Grün', value: '#22c55e' },
  yellow: { label: 'Gelb', value: '#eab308' },
  orange: { label: 'Orange', value: '#f97316' },
  red: { label: 'Rot', value: '#ef4444' },
  purple: { label: 'Lila', value: '#a855f7' },
  pink: { label: 'Pink', value: '#ec4899' },
} as const

export type ColumnColor = keyof typeof columnColors

export type ColumnChanges = Partial<Pick<KanbanColumn, 'name' | 'color'>>

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
