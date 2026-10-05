import { devIdentityEnabled, getDevUser } from '../identity/devUser'
import { accessToken } from '../identity/signIn'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

function messageOf(problem: ProblemDetails | null, status: number): string {
  const validation = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined
  return validation ?? problem?.detail ?? problem?.title ?? `Anfrage fehlgeschlagen (${status})`
}

async function send(path: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body !== undefined && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const devUser = devIdentityEnabled ? getDevUser() : null
  if (devUser) headers.set('X-Dev-User', devUser)
  const token = await accessToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(path, { ...init, headers })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as ProblemDetails | null
    throw new ApiError(response.status, messageOf(problem, response.status))
  }

  return response
}

/** Calls the ProjectHub API with the Entra ID access token, or in development the selected synthetic user. */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Sends a request with the same authentication and hands back the open response, e.g. to read a server-sent event stream. */
export const apiStream = (path: string, init: RequestInit = {}): Promise<Response> => send(path, init)

/** Downloads a file through the API (with the same authentication) and hands it to the browser. */
export async function apiDownload(path: string, fileName: string): Promise<void> {
  const blob = await (await send(path, {})).blob()
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

/** File name like the server's: no path or reserved characters, at most 60 characters. */
export function safeFileName(title: string, fallback: string): string {
  const cleaned = title
    .replace(/[\\/:*?"<>|\p{Cc}]/gu, ' ')
    .split(' ')
    .filter(Boolean)
    .join(' ')
    .slice(0, 60)
    .trim()
  return cleaned || fallback
}

/** Today as ISO date (yyyy-mm-dd), for file names. */
export const isoToday = () => new Date().toISOString().slice(0, 10)

export const jsonBody = (value: unknown) => JSON.stringify(value)

export type Paged<T> = { items: T[]; nextOffset: number | null }
