import { apiFetch } from '../api/client'

export type Me = {
  id: string
  displayName: string
  email: string
  organizationId: string
  organizationRole: 'admin' | 'member'
}

export type Organization = { id: string; name: string; slug: string }

export type DevUser = { objectId: string; displayName: string; organization: string; organizationRole: string }

export type User = { id: string; displayName: string; email: string; department: string | null; status: string }

export const fetchMe = () => apiFetch<Me>('/api/v1/me')
export const fetchOrganization = () => apiFetch<Organization>('/api/v1/organization')
export const fetchDevUsers = () => apiFetch<DevUser[]>('/api/dev/users')
