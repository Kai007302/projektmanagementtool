import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { ProjectDetails } from '../projects/api'
import {
  addWebexLink,
  createWebexSpace,
  fetchProjectWebex,
  isWebexUrl,
  removeWebexLink,
  webexKinds,
  type ProjectWebex,
  type WebexLinkKind,
} from './api'
import { useLatest } from '../api/useLatest'
import { Reveal } from '../ui/Reveal'
import { toast } from '../ui/toast'

type Props = { project: ProjectDetails; revision: number; onChanged: () => void }

/** Webex meetings and spaces of a project (ADR 0011). Links open in Webex; ProjectHub keeps only the link. */
export function WebexPanel({ project, revision, onChanged }: Props) {
  const [webex, setWebex] = useState<ProjectWebex | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [kind, setKind] = useState<WebexLinkKind>('meeting')
  const [title, setTitle] = useState('')
  const [url, setUrl] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const { canEdit } = project.capabilities

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchProjectWebex(project.id)).then(setWebex, (e: Error) => setError(e.message))
  }, [latest, project.id])

  useEffect(load, [load, revision])

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
      load()
    }
  }

  async function add(event: FormEvent, close: () => void) {
    event.preventDefault()
    setFormError(null)
    if (!isWebexUrl(url)) {
      setFormError('Bitte einen https-Link auf webex.com eingeben.')
      return
    }
    setBusy(true)
    try {
      await addWebexLink(project.id, kind, title, url)
      setTitle('')
      setUrl('')
      close()
      toast('Webex-Link gespeichert.')
      onChanged()
      load()
    } catch (e) {
      setFormError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (webex && !webex.available && webex.links.length === 0) return null

  const hasSpace = webex?.links.some((link) => link.createdByBot && link.status === 'active') ?? false

  return (
    <section className="panel webex" aria-labelledby="webex-heading">
      <h3 id="webex-heading">Webex</h3>
      {error && <p role="alert">{error}</p>}
      {webex === null ? (
        <p>Wird geladen …</p>
      ) : webex.links.length === 0 ? (
        <p className="muted">Noch keine Meetings oder Spaces verknüpft.</p>
      ) : (
        <ul className="plain-list webex-links">
          {webex.links.map((link) => (
            <li key={link.id}>
              <a href={link.url} target="_blank" rel="noopener noreferrer" aria-label={`${webexKinds[link.kind]} „${link.title}“ in Webex öffnen (neues Fenster)`}>
                {link.title}
              </a>{' '}
              <small className="muted">
                {webexKinds[link.kind]}
                {link.createdByBot && ' · Projektraum'}
              </small>
              {link.status === 'disconnected' && <small className="warning"> · getrennt: der Bot wurde aus dem Raum entfernt</small>}
              {canEdit && (
                <button
                  type="button"
                  className="link-button"
                  aria-label={`Verknüpfung „${link.title}“ entfernen`}
                  disabled={busy}
                  onClick={() => {
                    if (window.confirm(`Verknüpfung „${link.title}“ entfernen? In Webex bleibt alles erhalten.`)) void run(() => removeWebexLink(link.id))
                  }}
                >
                  Entfernen
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {canEdit && webex?.available && !hasSpace && (
        <button type="button" disabled={busy} onClick={() => void run(() => createWebexSpace(project.id))}>
          Projektraum in Webex anlegen
        </button>
      )}
      {canEdit && webex && (
        <Reveal label="Webex-Link">
          {(close) => (
            <form className="webex-form" aria-label="Webex-Link hinzufügen" onSubmit={(event) => void add(event, close)}>
              <label>
                Art
                <select value={kind} onChange={(event) => setKind(event.target.value as WebexLinkKind)} autoFocus>
                  {Object.entries(webexKinds).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Titel
                <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={200} />
              </label>
              <label>
                Link
                <input type="url" value={url} onChange={(event) => setUrl(event.target.value)} required placeholder="https://firma.webex.com/meet/…" />
              </label>
              <button type="submit" disabled={busy}>
                Hinzufügen
              </button>
              {formError && <p role="alert">{formError}</p>}
            </form>
          )}
        </Reveal>
      )}
    </section>
  )
}
