import { useEffect, useState } from 'react'
import { dismiss, subscribe, type Toast } from './toast'

/** Shows the notes from `toast`; rendered once in the app shell (and in tests that check a note). */
export function Toaster() {
  const [toasts, setToasts] = useState<Toast[]>([])
  useEffect(() => subscribe(setToasts), [])
  return (
    <div className="toasts" role="status" aria-live="polite">
      {toasts.map((t) => (
        <p key={t.id} className="toast">
          <span className="toast-check" aria-hidden="true">
            ✓
          </span>
          {t.text}
          <button type="button" className="icon-button" aria-label="Meldung schließen" onClick={() => dismiss(t.id)}>
            ✕
          </button>
        </p>
      ))}
    </div>
  )
}
