import { apiFetch, jsonBody, type Paged } from '../api/client'
import type { TaskStatus } from '../tasks/api'

export type Whiteboard = { id: string; projectId: string; name: string; createdAt: string; updatedAt: string; version: number }

/** Live data of a task card; never stored in the whiteboard document. */
export type WhiteboardTask = { id: string; title: string; status: TaskStatus; assigneeName: string | null; dueDate: string | null }

export type TaskWhiteboard = { id: string; name: string }

export const fetchWhiteboards = (projectId: string) => apiFetch<Paged<Whiteboard>>(`/api/v1/projects/${projectId}/whiteboards?limit=100`)

export const fetchWhiteboard = (id: string) => apiFetch<Whiteboard>(`/api/v1/whiteboards/${id}`)

export const createWhiteboard = (projectId: string, name: string) =>
  apiFetch<Whiteboard>(`/api/v1/projects/${projectId}/whiteboards`, { method: 'POST', body: jsonBody({ name }) })

export const renameWhiteboard = (board: Whiteboard, name: string) =>
  apiFetch<Whiteboard>(`/api/v1/whiteboards/${board.id}`, { method: 'PATCH', body: jsonBody({ version: board.version, name }) })

export const deleteWhiteboard = (id: string) => apiFetch<void>(`/api/v1/whiteboards/${id}`, { method: 'DELETE' })

export const fetchWhiteboardTasks = (id: string, taskIds: string[]) =>
  taskIds.length === 0
    ? Promise.resolve([] as WhiteboardTask[])
    : apiFetch<WhiteboardTask[]>(`/api/v1/whiteboards/${id}/tasks?ids=${taskIds.slice(0, 100).join(',')}`)

export const fetchTaskWhiteboards = (taskId: string) => apiFetch<TaskWhiteboard[]>(`/api/v1/tasks/${taskId}/whiteboards`)
