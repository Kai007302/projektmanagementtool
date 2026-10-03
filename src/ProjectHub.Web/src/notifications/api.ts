import { apiFetch, jsonBody, type Paged } from '../api/client'

export type NotificationType = 'task_assigned' | 'task_comment_mention' | 'knowledge_comment_mention' | 'project_member_added'

export type Notification = {
  id: string
  type: NotificationType
  title: string
  body: string | null
  resourceType: 'task' | 'knowledge_article' | 'project' | null
  resourceId: string | null
  projectId: string | null
  createdAt: string
  readAt: string | null
}

export type NotificationPreferences = { inAppEnabled: boolean; emailEnabled: boolean }

export const fetchNotifications = () => apiFetch<Paged<Notification>>('/api/v1/me/notifications?limit=30')

export const fetchUnreadCount = () => apiFetch<{ count: number }>('/api/v1/me/notifications/unread-count')

export const markRead = (id: string) => apiFetch<void>(`/api/v1/notifications/${id}/read`, { method: 'POST' })

export const markAllRead = () => apiFetch<void>('/api/v1/me/notifications/read-all', { method: 'POST' })

export const fetchPreferences = () => apiFetch<NotificationPreferences>('/api/v1/me/notification-preferences')

export const savePreferences = (preferences: NotificationPreferences) =>
  apiFetch<NotificationPreferences>('/api/v1/me/notification-preferences', { method: 'PUT', body: jsonBody(preferences) })
