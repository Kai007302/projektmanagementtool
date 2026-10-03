import { apiFetch, type Paged } from '../api/client'
import type { User } from '../identity/api'

export type TeamSummary = { id: string; name: string; description: string | null; memberCount: number }

export type TeamMember = { userId: string; displayName: string; email: string; role: 'owner' | 'member' }

export type TeamDetails = {
  id: string
  name: string
  description: string | null
  canManage: boolean
  members: TeamMember[]
}

export const fetchTeams = () => apiFetch<Paged<TeamSummary>>('/api/v1/teams?limit=100')

export const fetchTeam = (id: string) => apiFetch<TeamDetails>(`/api/v1/teams/${id}`)

export const createTeam = (name: string, description: string) =>
  apiFetch<TeamSummary>('/api/v1/teams', { method: 'POST', body: JSON.stringify({ name, description }) })

export const addTeamMember = (teamId: string, userId: string, role: TeamMember['role']) =>
  apiFetch<void>(`/api/v1/teams/${teamId}/members`, { method: 'POST', body: JSON.stringify({ userId, role }) })

export const removeTeamMember = (teamId: string, userId: string) =>
  apiFetch<void>(`/api/v1/teams/${teamId}/members/${userId}`, { method: 'DELETE' })

export const fetchUsers = () => apiFetch<Paged<User>>('/api/v1/users?limit=100')
