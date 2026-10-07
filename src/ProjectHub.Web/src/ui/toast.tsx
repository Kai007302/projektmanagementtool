import { useEffect, useState } from 'react'

type Toast = { id: number; text: string }

let nextId = 1
let current: Toast[] = []
const listeners = new Set<(toasts: Toast[]) => void>()

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
  setTimeout(() => publish(current.filter((t) => t.id !== id)), 5000)
}

/** Shows the notes from `toast`; rendered once in the app shell (and in tests that check a note). */
export function Toaster() {
  const [toasts, setToasts] = useState(current)
  useEffect(() => {
    listeners.add(setToasts)
    return () => void listeners.delete(setToasts)
  }, [])
  return (
    <div className="toasts" role="status" aria-live="polite">
      {toasts.map((t) => (
        <p key={t.id} className="toast">
          <span className="toast-check" aria-hidden="true">
            ✓
          </span>
          {t.text}
          <button type="button" className="icon-button" aria-label="Meldung schließen" onClick={() => publish(current.filter((other) => other.id !== t.id))}>
            ✕
          </button>
        </p>
      ))}
    </div>
  )
}
