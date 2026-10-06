import { apiFetch, jsonBody, type Paged } from '../api/client'

export type ProjectRole = 'admin' | 'editor' | 'member' | 'viewer' | 'guest'
export type ProjectStatus = 'planned' | 'active' | 'on_hold' | 'completed' | 'archived'

export type ProjectSummary = {
  id: string
  name: string
  description: string | null
  status: ProjectStatus
  startDate: string | null
  endDate: string | null
  myRole: ProjectRole | null
  version: number
  /** The emoji chosen for the project; null shows one derived from the id. */
  icon?: string | null
  /** Set while the project has a logo; changes with every new logo. */
  logoVersion?: number | null
}

export type ProjectMember = { userId: string; displayName: string; email: string; role: ProjectRole }

export type Capabilities = { canContribute: boolean; canEdit: boolean; canManage: boolean }

export type ProjectDetails = Omit<ProjectSummary, 'myRole'> & {
  ownerId: string
  capabilities: Capabilities
  members: ProjectMember[]
}

export type Activity = {
  id: string
  action: string
  resourceType: string
  resourceId: string | null
  actorName: string | null
  metadata: Record<string, unknown>
  createdAt: string
}

export const projectRoles: Record<ProjectRole, string> = {
  admin: 'Admin',
  editor: 'Editor',
  member: 'Mitglied',
  viewer: 'Leser',
  guest: 'Gast',
}

export const projectStatuses: Record<ProjectStatus, string> = {
  planned: 'Geplant',
  active: 'Aktiv',
  on_hold: 'Pausiert',
  completed: 'Abgeschlossen',
  archived: 'Archiviert',
}

export const fetchProjects = () => apiFetch<Paged<ProjectSummary>>('/api/v1/projects?limit=100')

export const fetchProject = (id: string) => apiFetch<ProjectDetails>(`/api/v1/projects/${id}`)

export const createProject = (name: string, description: string) =>
  apiFetch<ProjectSummary>('/api/v1/projects', { method: 'POST', body: jsonBody({ name, description }) })

export const updateProject = (id: string, version: number, changes: Partial<Pick<ProjectSummary, 'name' | 'description' | 'status' | 'icon'>>) =>
  apiFetch<ProjectSummary>(`/api/v1/projects/${id}`, { method: 'PATCH', body: jsonBody({ version, ...changes }) })

export function uploadProjectLogo(id: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  return apiFetch<{ logoVersion: number | null }>(`/api/v1/projects/${id}/logo`, { method: 'PUT', body: form })
}

export const deleteProjectLogo = (id: string) => apiFetch<{ logoVersion: number | null }>(`/api/v1/projects/${id}/logo`, { method: 'DELETE' })

/** Roles that may change a project's name, symbol and logo. */
export const canEditProject = (role: ProjectRole | null) => role === 'admin' || role === 'editor'

export const deleteProject = (id: string) => apiFetch<void>(`/api/v1/projects/${id}`, { method: 'DELETE' })

export const addProjectMember = (id: string, userId: string, role: ProjectRole) =>
  apiFetch<void>(`/api/v1/projects/${id}/members`, { method: 'POST', body: jsonBody({ userId, role }) })

export const changeProjectMember = (id: string, userId: string, role: ProjectRole) =>
  apiFetch<void>(`/api/v1/projects/${id}/members/${userId}`, { method: 'PATCH', body: jsonBody({ role }) })

export const removeProjectMember = (id: string, userId: string) =>
  apiFetch<void>(`/api/v1/projects/${id}/members/${userId}`, { method: 'DELETE' })

export const fetchActivity = (id: string) => apiFetch<Paged<Activity>>(`/api/v1/projects/${id}/activity?limit=30`)
