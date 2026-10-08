import { apiFetch, type Paged } from '../api/client'

export type Me = {
  id: string
  displayName: string
  email: string
  organizationId: string
  organizationRole: 'admin' | 'member'
  /** The caller's own departments with their role there (ADR 0021). */
  departments: MyDepartment[]
}

export type DepartmentRole = 'lead' | 'member' | 'guest'

export type MyDepartment = { id: string; name: string; role: DepartmentRole }

export type Organization = { id: string; name: string; slug: string }

export type DevUser = { objectId: string; displayName: string; organization: string; organizationRole: string }

export type User = { id: string; displayName: string; email: string; department: string | null; status: string }

export const fetchMe = () => apiFetch<Me>('/api/v1/me')
export const fetchOrganization = () => apiFetch<Organization>('/api/v1/organization')
export const fetchDevUsers = () => apiFetch<DevUser[]>('/api/dev/users')
export const fetchUsers = () => apiFetch<Paged<User>>('/api/v1/users?limit=100')
