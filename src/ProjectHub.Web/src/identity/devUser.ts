const storageKey = 'projecthub.devUser'

/**
 * Choosing a synthetic user exists in the dev server and in builds made with VITE_DEV_IDENTITY=true (end-to-end
 * tests against the containers). Only an API in Development honours it; elsewhere Entra ID decides.
 */
export const devIdentityEnabled: boolean = import.meta.env.DEV || import.meta.env.VITE_DEV_IDENTITY === 'true'

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
