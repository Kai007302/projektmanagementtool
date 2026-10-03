import { getDevUser } from '../identity/devUser'

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

/** Calls the ProjectHub API. In development the selected synthetic user is sent along. */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body !== undefined) headers.set('Content-Type', 'application/json')
  const devUser = import.meta.env.DEV ? getDevUser() : null
  if (devUser) headers.set('X-Dev-User', devUser)

  const response = await fetch(path, { ...init, headers })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as ProblemDetails | null
    throw new ApiError(response.status, messageOf(problem, response.status))
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export type Paged<T> = { items: T[]; nextOffset: number | null }
