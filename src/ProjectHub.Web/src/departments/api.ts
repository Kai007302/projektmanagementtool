import { apiFetch, jsonBody, type Paged } from '../api/client'
import type { DepartmentRole, User } from '../identity/api'

export type DepartmentSummary = {
  id: string
  name: string
  description: string | null
  memberCount: number
  myRole: DepartmentRole | null
  canManage: boolean
  /** Only shown to those who manage the department. */
  entraGroupId: string | null
  version: number
}

export type DepartmentMember = { userId: string; displayName: string; email: string; role: DepartmentRole; source: 'manual' | 'entra' }

export type DepartmentDetails = Omit<DepartmentSummary, 'memberCount'> & { members: DepartmentMember[] }

export const departmentRoles: Record<DepartmentRole, string> = { lead: 'Leitung', member: 'Mitglied', guest: 'Gast' }

export const fetchDepartments = () => apiFetch<Paged<DepartmentSummary>>('/api/v1/departments?limit=100')

export const fetchDepartment = (id: string) => apiFetch<DepartmentDetails>(`/api/v1/departments/${id}`)

export const createDepartment = (name: string, description: string, entraGroupId: string) =>
  apiFetch<DepartmentSummary>('/api/v1/departments', {
    method: 'POST',
    body: jsonBody({ name, description: description || null, entraGroupId: entraGroupId || null }),
  })

export const updateDepartment = (id: string, version: number, changes: Partial<{ name: string; description: string | null; entraGroupId: string | null }>) =>
  apiFetch<DepartmentSummary>(`/api/v1/departments/${id}`, { method: 'PATCH', body: jsonBody({ version, ...changes }) })

export const deleteDepartment = (id: string) => apiFetch<void>(`/api/v1/departments/${id}`, { method: 'DELETE' })

export const addDepartmentMember = (id: string, userId: string, role: DepartmentRole) =>
  apiFetch<void>(`/api/v1/departments/${id}/members`, { method: 'POST', body: jsonBody({ userId, role }) })

export const changeDepartmentMember = (id: string, userId: string, role: DepartmentRole) =>
  apiFetch<void>(`/api/v1/departments/${id}/members/${userId}`, { method: 'PATCH', body: jsonBody({ role }) })

export const removeDepartmentMember = (id: string, userId: string) =>
  apiFetch<void>(`/api/v1/departments/${id}/members/${userId}`, { method: 'DELETE' })

/** People of the organization in no department yet; for organization admins and department leads. */
export const fetchUnassigned = () => apiFetch<User[]>('/api/v1/departments/unassigned')
