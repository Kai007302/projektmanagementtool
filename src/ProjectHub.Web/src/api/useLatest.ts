import { useState } from 'react'

export type Latest = {
  <T>(request: Promise<T>): Promise<T>
  /** A local change is newer than every load still on its way; their answers are dropped. */
  invalidate: () => void
}

function createLatest(): Latest {
  let latest = 0
  const track = <T,>(request: Promise<T>): Promise<T> => {
    const id = ++latest
    return request.then(
      (value) => (id === latest ? value : new Promise<T>(() => {})),
      (error: unknown) => (id === latest ? Promise.reject(error) : new Promise<T>(() => {})),
    )
  }
  return Object.assign(track, {
    invalidate: () => {
      latest++
    },
  })
}

/**
 * Wraps loads that can overlap (realtime reloads, own changes) so only the newest one is applied. Older answers
 * never settle: under load or behind a proxy they may arrive last and would otherwise show stale data.
 */
export function useLatest(): Latest {
  const [latest] = useState(createLatest)
  return latest
}
