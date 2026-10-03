const storageKey = 'projecthub.devUser'

/** Object id of the synthetic user chosen for development sign-in, if any. */
export function getDevUser(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

export function setDevUser(objectId: string): void {
  try {
    localStorage.setItem(storageKey, objectId)
  } catch {
    // Storage unavailable: the backend's default development user stays signed in.
  }
}
