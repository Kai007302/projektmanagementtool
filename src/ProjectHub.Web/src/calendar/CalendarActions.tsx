import { useState } from 'react'
import { outlookComposeUrl } from './calendar'

type Props = {
  title: string
  firstDay: string
  lastDay: string
  /** What the entry is, for screen readers ("Aufgabe", "Meilenstein"). */
  kind: string
  onDownload: () => Promise<void>
}

/** Puts a task or milestone into the person's own calendar: as .ics file or through Outlook on the web. */
export function CalendarActions({ title, firstDay, lastDay, kind, onDownload }: Props) {
  const [error, setError] = useState<string | null>(null)

  return (
    <span className="calendar-actions">
      <button
        type="button"
        className="link-button"
        aria-label={`${kind} „${title}“ als Kalenderdatei herunterladen`}
        onClick={() => {
          setError(null)
          onDownload().catch((e: Error) => setError(e.message))
        }}
      >
        Kalenderdatei (.ics)
      </button>
      <a
        className="link-button"
        href={outlookComposeUrl(title, firstDay, lastDay, window.location.origin)}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`${kind} „${title}“ in Outlook im Web anlegen (neues Fenster)`}
      >
        In Outlook anlegen
      </a>
      {error && <span role="alert">{error}</span>}
    </span>
  )
}
