/** What a person opened last (projects, articles, whiteboards), for "Weiter, wo du warst" on the start page. */
export type Visit =
  | { kind: 'project'; id: string; title: string }
  | { kind: 'article'; id: string; title: string }
  | { kind: 'whiteboard'; id: string; title: string; projectId: string }

const key = (userId: string) => `projecthub.recent.${userId}`
const LIMIT = 6

/** The last visits of this person in this browser, newest first; empty when storage is blocked. */
export function recentVisits(userId: string): Visit[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(key(userId)) ?? '[]')
    return Array.isArray(value) ? (value as Visit[]) : []
  } catch {
    return []
  }
}

export function rememberVisit(userId: string, visit: Visit) {
  const rest = recentVisits(userId).filter((v) => !(v.kind === visit.kind && v.id === visit.id))
  try {
    localStorage.setItem(key(userId), JSON.stringify([visit, ...rest].slice(0, LIMIT)))
  } catch {
    // Storage may be blocked; the start page then simply shows no recent items.
  }
}

/** Removes an entry that no longer opens (deleted, or access withdrawn). */
export function forgetVisit(userId: string, visit: Pick<Visit, 'kind' | 'id'>) {
  try {
    localStorage.setItem(key(userId), JSON.stringify(recentVisits(userId).filter((v) => !(v.kind === visit.kind && v.id === visit.id))))
  } catch {
    // See rememberVisit.
  }
}
