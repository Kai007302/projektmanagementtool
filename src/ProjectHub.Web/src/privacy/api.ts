import { apiDownload, apiFetch, type Paged } from '../api/client'
import type { User } from '../identity/api'

/** Links the operator configured (ADR 0015); null when not set. */
export type LegalInformation = { privacyNoticeUrl: string | null; imprintUrl: string | null }

export const fetchLegal = () => apiFetch<LegalInformation>('/api/v1/legal')

const today = () => new Date().toISOString().slice(0, 10)

/** Everything ProjectHub stores about the signed-in person, as a JSON file (Art. 15/20 DSGVO). */
export const downloadMyData = () => apiDownload('/api/v1/me/data-export', `projecthub-meine-daten-${today()}.json`)

export const searchUsers = (search: string) =>
  apiFetch<Paged<User>>(`/api/v1/users?search=${encodeURIComponent(search)}&limit=10`)

export const anonymizeUser = (userId: string) => apiFetch<void>(`/api/v1/admin/users/${userId}/anonymize`, { method: 'POST' })
