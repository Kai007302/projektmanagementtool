import { apiDownload, apiFetch, jsonBody, type Paged } from '../api/client'

export type TaskStatus = 'todo' | 'in_progress' | 'done'
export type TaskPriority = 'low' | 'normal' | 'high' | 'urgent'

export type Task = {
  id: string
  projectId: string
  parentTaskId: string | null
  title: string
  description: string | null
  status: TaskStatus
  priority: TaskPriority
  assigneeId: string | null
  assigneeName: string | null
  startDate: string | null
  dueDate: string | null
  progress: number
  estimatedHours: number | null
  subtaskCount: number
  version: number
}

export type TaskChanges = Partial<
  Pick<Task, 'title' | 'description' | 'status' | 'priority' | 'assigneeId' | 'startDate' | 'dueDate' | 'progress'>
>

export type Comment = {
  id: string
  authorId: string
  authorName: string
  content: string
  createdAt: string
  version: number
}

export type Attachment = {
  id: string
  fileName: string
  sizeBytes: number
  uploadedBy: string
  uploadedByName: string
  createdAt: string
}

export const taskStatuses: Record<TaskStatus, string> = { todo: 'Offen', in_progress: 'In Arbeit', done: 'Erledigt' }

export const taskPriorities: Record<TaskPriority, string> = { low: 'Niedrig', normal: 'Normal', high: 'Hoch', urgent: 'Dringend' }

/** Emoji per priority: quick to scan on cards. */
export const priorityEmoji: Record<TaskPriority, string> = { low: '🌿', normal: '📌', high: '🔥', urgent: '🚨' }

/** All tasks of a project (top level and subtasks), at most 100 for now. */
export const fetchTasks = (projectId: string) => apiFetch<Paged<Task>>(`/api/v1/projects/${projectId}/tasks?limit=100`)

export const fetchTask = (id: string) => apiFetch<Task>(`/api/v1/tasks/${id}`)

export const createTask = (projectId: string, title: string, parentTaskId: string | null, status?: TaskStatus) =>
  apiFetch<Task>(`/api/v1/projects/${projectId}/tasks`, { method: 'POST', body: jsonBody({ title, parentTaskId, ...(status && { status }) }) })

export const updateTask = (id: string, version: number, changes: TaskChanges) =>
  apiFetch<Task>(`/api/v1/tasks/${id}`, { method: 'PATCH', body: jsonBody({ version, ...changes }) })

export const deleteTask = (id: string) => apiFetch<void>(`/api/v1/tasks/${id}`, { method: 'DELETE' })

export const fetchComments = (taskId: string) => apiFetch<Paged<Comment>>(`/api/v1/tasks/${taskId}/comments?limit=100`)

export const addComment = (taskId: string, content: string, mentionedUserIds: string[]) =>
  apiFetch<Comment>(`/api/v1/tasks/${taskId}/comments`, { method: 'POST', body: jsonBody({ content, mentionedUserIds }) })

export const deleteComment = (id: string) => apiFetch<void>(`/api/v1/comments/${id}`, { method: 'DELETE' })

export const fetchAttachments = (taskId: string) => apiFetch<Attachment[]>(`/api/v1/tasks/${taskId}/attachments`)

export function uploadAttachment(taskId: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  return apiFetch<Attachment>(`/api/v1/tasks/${taskId}/attachments`, { method: 'POST', body: form })
}

export const downloadAttachment = (attachment: Attachment) =>
  apiDownload(`/api/v1/attachments/${attachment.id}/content`, attachment.fileName)

export const deleteAttachment = (id: string) => apiFetch<void>(`/api/v1/attachments/${id}`, { method: 'DELETE' })
