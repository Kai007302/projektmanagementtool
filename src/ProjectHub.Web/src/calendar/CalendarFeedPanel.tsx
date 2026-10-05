import { useEffect, useId, useState } from 'react'
import { createCalendarFeed, deleteCalendarFeed, fetchCalendarFeed, webcalUrl, type CalendarFeedStatus } from './calendar'

const dateText = (iso: string) => new Date(iso).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

/**
 * The person's own dates as a calendar subscription (ADR 0018): tasks assigned to them with a date and the
 * milestones of their projects. The address works like a password and is shown only right after it is created.
 */
export function CalendarFeedPanel() {
  const [status, setStatus] = useState<CalendarFeedStatus | null>(null)
  const [url, setUrl] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputId = useId()

  useEffect(() => {
    fetchCalendarFeed().then(setStatus, (e: Error) => setError(e.message))
  }, [])

  async function create() {
    if (status?.active && !window.confirm('Neue Adresse erstellen? Die bisherige funktioniert dann nicht mehr.')) return
    setError(null)
    try {
      const created = await createCalendarFeed()
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
      await deleteCalendarFeed()
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
      <h3 id="calendar-feed-heading">Kalender abonnieren</h3>
      <p className="muted">
        Deine Aufgaben mit Termin und die Meilensteine deiner Projekte erscheinen in Outlook, Google oder Apple Kalender und
        aktualisieren sich von selbst.
      </p>
      {status === null && !error && <p>Wird geladen …</p>}
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
            Wer sie kennt, sieht Titel und Projekt deiner Termine. Sie wird nur jetzt angezeigt.
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
