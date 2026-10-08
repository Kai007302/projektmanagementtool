import { apiFetch, jsonBody, type Paged } from '../api/client'

export type ProjectRole = 'admin' | 'editor' | 'member' | 'viewer' | 'guest'
export type ProjectStatus = 'planned' | 'active' | 'on_hold' | 'completed' | 'archived'
export type ProjectVisibility = 'private' | 'department' | 'organization'

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
  /** The department the project belongs to (ADR 0021). */
  departmentId?: string | null
  visibility?: ProjectVisibility | null
}

export type ProjectMember = { userId: string; displayName: string; email: string; role: ProjectRole }

/** canShareWithOrganization: organization admins and leads of the project's department (ADR 0021). */
export type Capabilities = { canContribute: boolean; canEdit: boolean; canManage: boolean; canShareWithOrganization?: boolean }

export type ProjectDetails = Omit<ProjectSummary, 'myRole'> & {
  ownerId: string
  capabilities: Capabilities
  members: ProjectMember[]
  departmentName?: string | null
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

export const projectVisibilities: Record<ProjectVisibility, { label: string; hint: string }> = {
  private: { label: 'Privat', hint: 'Nur die Mitglieder des Projekts' },
  department: { label: 'Abteilung', hint: 'Alle der Abteilung lesen mit' },
  organization: { label: 'Organisation', hint: 'Alle in der Organisation lesen mit' },
}

/** The projects I can see; with a department only those of that department. */
export const fetchProjects = (departmentId = '') =>
  apiFetch<Paged<ProjectSummary>>(`/api/v1/projects?limit=100${departmentId ? `&departmentId=${departmentId}` : ''}`)

export const fetchProject = (id: string) => apiFetch<ProjectDetails>(`/api/v1/projects/${id}`)

/** Without a department the project goes to my first department. */
export const createProject = (name: string, description: string, departmentId = '') =>
  apiFetch<ProjectSummary>('/api/v1/projects', { method: 'POST', body: jsonBody({ name, description, departmentId: departmentId || null }) })

export const updateProject = (
  id: string,
  version: number,
  changes: Partial<Pick<ProjectSummary, 'name' | 'description' | 'status' | 'icon' | 'departmentId' | 'visibility'>>,
) =>
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
