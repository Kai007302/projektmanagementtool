import { useEffect, useId, useState } from 'react'
import { createCalendarFeed, deleteCalendarFeed, fetchCalendarFeed, webcalUrl, type CalendarFeedStatus } from './calendar'
import { Skeleton } from '../ui/Skeleton'

const dateText = (iso: string) => new Date(iso).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

/**
 * A calendar subscription (ADR 0018): without a project the person's own dates (tasks assigned to them with a date and
 * the milestones of their projects), with one all dates of that project (ADR 0020). The address works like a password
 * and is shown only right after it is created.
 */
export function CalendarFeedPanel({ project }: { project?: { id: string; name: string } }) {
  const [status, setStatus] = useState<CalendarFeedStatus | null>(null)
  const [url, setUrl] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputId = useId()

  useEffect(() => {
    fetchCalendarFeed(project?.id).then(setStatus, (e: Error) => setError(e.message))
  }, [project?.id])

  async function create() {
    if (status?.active && !window.confirm('Neue Adresse erstellen? Die bisherige funktioniert dann nicht mehr.')) return
    setError(null)
    try {
      const created = await createCalendarFeed(project?.id)
      setUrl(created.url)
      setCopied(false)
      setStatus({ active: true, createdAt: created.createdAt, lastUsedAt: null })
    } catch (e) {
      setError((e as Error).message)
    }
  }

  async function remove() {
    if (!window.confirm('Kalender-Adresse löschen? Abonnierte Kalender werden dann nicht mehr aktualisiert.')) return
    setError(null)
    try {
      await deleteCalendarFeed(project?.id)
      setUrl(null)
      setStatus({ active: false, createdAt: null, lastUsedAt: null })
    } catch (e) {
      setError((e as Error).message)
    }
  }

  async function copy() {
    if (!url) return
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
    } catch {
      setError('Kopieren ging nicht. Bitte die Adresse markieren und selbst kopieren.')
    }
  }

  return (
    <section className="panel calendar-feed" aria-labelledby="calendar-feed-heading">
      <h3 id="calendar-feed-heading">{project ? `Kalender von „${project.name}“ abonnieren` : 'Meine Termine abonnieren'}</h3>
      <p className="muted">
        {project
          ? 'Alle Aufgaben mit Termin und die Meilensteine dieses Projekts erscheinen in Outlook, Google oder Apple Kalender und aktualisieren sich von selbst.'
          : 'Deine Aufgaben mit Termin und die Meilensteine deiner Projekte erscheinen in Outlook, Google oder Apple Kalender und aktualisieren sich von selbst.'}
      </p>
      {status === null && !error && <Skeleton count={2} label="Kalender wird geladen" />}
      {url && (
        <div className="calendar-feed-url">
          <label htmlFor={inputId}>Deine Kalender-Adresse</label>
          <input id={inputId} readOnly value={url} onFocus={(event) => event.target.select()} />
          <div className="row">
            <button type="button" onClick={() => void copy()}>
              {copied ? 'Kopiert ✓' : 'Kopieren'}
            </button>
            <a className="link-button" href={webcalUrl(url)}>
              In Kalender-App öffnen
            </a>
          </div>
          <p className="muted">
            In Outlook: Kalender hinzufügen → Aus dem Internet abonnieren → Adresse einfügen. Die Adresse wirkt wie ein Passwort:
            Wer sie kennt, sieht die Titel der Termine. Sie wird nur jetzt angezeigt.
          </p>
        </div>
      )}
      {status && !status.active && (
        <button type="button" onClick={() => void create()}>
          Kalender-Adresse erstellen
        </button>
      )}
      {status?.active && (
        <>
          {!url && status.createdAt && (
            <p>
              Adresse aktiv seit {dateText(status.createdAt)}
              {status.lastUsedAt ? `, zuletzt abgerufen ${dateText(status.lastUsedAt)}.` : ', noch nicht abgerufen.'}
            </p>
          )}
          <div className="row">
            <button type="button" onClick={() => void create()}>
              Neue Adresse erstellen
            </button>
            <button type="button" className="danger" onClick={() => void remove()}>
              Adresse löschen
            </button>
          </div>
        </>
      )}
      {error && <p role="alert">{error}</p>}
    </section>
  )
}
