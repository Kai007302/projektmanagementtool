import { apiFetch, jsonBody } from '../api/client'

export type WebexLinkKind = 'meeting' | 'space'

export type WebexLink = {
  id: string
  kind: WebexLinkKind
  title: string
  url: string
  status: 'active' | 'disconnected'
  createdByBot: boolean
  createdAt: string
}

export type ProjectWebex = { available: boolean; links: WebexLink[] }

export const webexKinds: Record<WebexLinkKind, string> = { meeting: 'Meeting', space: 'Space' }

export const fetchProjectWebex = (projectId: string) => apiFetch<ProjectWebex>(`/api/v1/projects/${projectId}/webex`)

export const addWebexLink = (projectId: string, kind: WebexLinkKind, title: string, url: string) =>
  apiFetch<WebexLink>(`/api/v1/projects/${projectId}/webex/links`, { method: 'POST', body: jsonBody({ kind, title, url }) })

export const createWebexSpace = (projectId: string) =>
  apiFetch<WebexLink>(`/api/v1/projects/${projectId}/webex/space`, { method: 'POST' })

export const removeWebexLink = (id: string) => apiFetch<void>(`/api/v1/webex-links/${id}`, { method: 'DELETE' })

/** Same rule as the server: https links to webex.com or a subdomain of it. */
export function isWebexUrl(value: string): boolean {
  try {
    const url = new URL(value.trim())
    return (
      url.protocol === 'https:' &&
      url.username === '' &&
      url.port === '' &&
      (url.hostname === 'webex.com' || url.hostname.endsWith('.webex.com'))
    )
  } catch {
    return false
  }
}
