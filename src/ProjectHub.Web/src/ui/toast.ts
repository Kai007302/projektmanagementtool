export type Toast = { id: number; text: string }

let nextId = 1
let current: Toast[] = []
const listeners = new Set<(toasts: Toast[]) => void>()

export function dismiss(id: number) {
  publish(current.filter((t) => t.id !== id))
}

export function subscribe(listener: (toasts: Toast[]) => void) {
  listeners.add(listener)
  listener(current)
  return () => void listeners.delete(listener)
}

function publish(toasts: Toast[]) {
  current = toasts
  for (const listener of listeners) listener(current)
}

/**
 * Confirms that something worked with a short note at the bottom right, e.g. "Projekt angelegt". It stays visible
 * while the page changes (a new project opens right away) and disappears by itself after a few seconds.
 */
export function toast(text: string) {
  const id = nextId++
  publish([...current, { id, text }].slice(-3))
  setTimeout(() => dismiss(id), 5000)
}

