import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import {
  articleStatuses,
  articleTypes,
  changeStatus,
  deleteArticle,
  fetchArticle,
  fetchSpaces,
  saveContent,
  updateArticle,
  visibilities,
  type ArticleChanges,
  type ArticleDetails,
  type ArticleStatus,
  type ArticleType,
  type Space,
  type Visibility,
} from './api'
import { ArticleComments, PermissionsPanel, ReferencesPanel, RelationsPanel, TagsPanel, VersionsPanel } from './ArticlePanels'
import { BlockEditor } from './BlockEditor'
import { BlockView } from './BlockView'
import type { Block } from './blocks'

type Props = { articleId: string; me: Me; startEditing?: boolean; onBack: () => void; onOpenArticle: (id: string) => void; onChanged: () => void }

/** Status changes the UI offers; the API checks the rights again (Edit for review, Admin for the rest). */
const transitions: Record<ArticleStatus, { to: ArticleStatus; label: string; admin: boolean }[]> = {
  draft: [
    { to: 'review', label: 'Zur Prüfung geben', admin: false },
    { to: 'published', label: 'Veröffentlichen', admin: true },
    { to: 'archived', label: 'Archivieren', admin: true },
  ],
  review: [
    { to: 'draft', label: 'Zurück in Entwurf', admin: false },
    { to: 'published', label: 'Veröffentlichen', admin: true },
    { to: 'archived', label: 'Archivieren', admin: true },
  ],
  published: [
    { to: 'draft', label: 'Zurück in Entwurf', admin: true },
    { to: 'archived', label: 'Archivieren', admin: true },
  ],
  archived: [{ to: 'draft', label: 'Wieder bearbeiten', admin: true }],
}

const conflictText = 'Jemand anderes hat den Artikel inzwischen geändert. Der aktuelle Stand wurde geladen.'

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

export function ArticleView({ articleId, me, startEditing = false, onBack, onOpenArticle, onChanged }: Props) {
  const [details, setDetails] = useState<ArticleDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(startEditing)
  const [revision, setRevision] = useState(0)

  const load = useCallback(() => {
    fetchArticle(articleId).then(setDetails, (e: Error) => setError(e.message))
  }, [articleId])

  useEffect(load, [load])

  const changed = useCallback(() => {
    load()
    setRevision((r) => r + 1)
    onChanged()
  }, [load, onChanged])

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(e instanceof ApiError && e.status === 409 ? conflictText : (e as Error).message)
    }
    changed()
  }

  async function remove() {
    if (!details || !window.confirm(`Artikel „${details.article.title}“ wirklich löschen?`)) return
    try {
      await deleteArticle(details.article.id)
      onChanged()
      onBack()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!details) {
    return (
      <section className="article-view">
        <button type="button" className="link-button" onClick={onBack}>
          ← Alle Artikel
        </button>
        {error ? <p role="alert">{error}</p> : <p>Artikel wird geladen …</p>}
      </section>
    )
  }

  const { article, capabilities } = details
  const actions = transitions[article.status].filter((t) => (t.admin ? capabilities.canAdmin : capabilities.canEdit))

  return (
    <section className="article-view" aria-labelledby="article-heading">
      <button type="button" className="link-button" onClick={onBack}>
        ← Alle Artikel
      </button>
      <header className="project-header">
        <div>
          <h2 id="article-heading">{article.title}</h2>
          <p className="muted">
            {articleTypes[article.articleType]} · <span className={`article-status status-${article.status}`}>{articleStatuses[article.status]}</span>
            {article.visibility === 'restricted' && ' · 🔒 eingeschränkt'}
            {article.spaceName && ` · ${article.spaceName}`}
            {article.ownerName && ` · verantwortlich: ${article.ownerName}`}
            {` · Version ${details.versionNumber}, ${dateFormat.format(new Date(article.updatedAt))}`}
          </p>
        </div>
        <div className="article-actions">
          {capabilities.canEdit && !editing && (
            <button type="button" onClick={() => setEditing(true)}>
              Bearbeiten
            </button>
          )}
          {actions.map((action) => (
            <button key={action.to} type="button" onClick={() => run(() => changeStatus(article.id, article.version, action.to))}>
              {action.label}
            </button>
          ))}
          {capabilities.canAdmin && (
            <button type="button" className="danger" onClick={remove}>
              Löschen
            </button>
          )}
        </div>
      </header>
      {error && <p role="alert">{error}</p>}
      <div className="project-layout">
        <div>
          {editing ? (
            <ArticleEditor
              details={details}
              onSaved={() => {
                setEditing(false)
                changed()
              }}
              onConflict={changed}
              onCancel={() => setEditing(false)}
            />
          ) : (
            <article className="panel">
              {article.summary && <p className="article-summary">{article.summary}</p>}
              <BlockView key={revision} blocks={details.content.blocks} onOpenArticle={onOpenArticle} />
            </article>
          )}
          <ArticleComments articleId={article.id} me={me} canModerate={capabilities.canAdmin} />
        </div>
        <aside className="project-side">
          <TagsPanel key={article.tags.join('|')} details={details} onChanged={changed} />
          <RelationsPanel details={details} onOpenArticle={onOpenArticle} onChanged={changed} />
          <ReferencesPanel details={details} onChanged={changed} />
          <VersionsPanel details={details} revision={revision} onChanged={changed} />
          {capabilities.canAdmin && <PermissionsPanel details={details} onChanged={changed} />}
        </aside>
      </div>
    </section>
  )
}

type EditorProps = { details: ArticleDetails; onSaved: () => void; onConflict: () => void; onCancel: () => void }

/** Edits metadata and content together; content is saved as a new version. */
function ArticleEditor({ details, onSaved, onConflict, onCancel }: EditorProps) {
  const { article } = details
  const [title, setTitle] = useState(article.title)
  const [summary, setSummary] = useState(article.summary ?? '')
  const [articleType, setArticleType] = useState<ArticleType>(article.articleType)
  const [spaceId, setSpaceId] = useState(article.spaceId ?? '')
  const [visibility, setVisibility] = useState<Visibility>(article.visibility)
  const [blocks, setBlocks] = useState<Block[]>(details.content.blocks)
  const [changeNote, setChangeNote] = useState('')
  const [spaces, setSpaces] = useState<Space[]>([])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetchSpaces().then(setSpaces, () => setSpaces([]))
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSaving(true)
    const changes: ArticleChanges = {}
    if (title !== article.title) changes.title = title
    if (summary !== (article.summary ?? '')) changes.summary = summary || null
    if (articleType !== article.articleType) changes.articleType = articleType
    if (spaceId !== (article.spaceId ?? '')) changes.spaceId = spaceId || null
    if (visibility !== article.visibility) changes.visibility = visibility
    try {
      let version = article.version
      if (Object.keys(changes).length > 0) version = (await updateArticle(article.id, version, changes)).article.version
      if (JSON.stringify(blocks) !== JSON.stringify(details.content.blocks)) await saveContent(article.id, version, blocks, changeNote)
      onSaved()
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError(conflictText)
        onConflict()
      } else {
        setError((e as Error).message)
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="panel stacked-form article-editor" onSubmit={submit} aria-label="Artikel bearbeiten">
      <div className="task-form stacked-form">
        <label>
          Titel
          <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={300} />
        </label>
        <label>
          Art
          <select value={articleType} onChange={(event) => setArticleType(event.target.value as ArticleType)}>
            {Object.entries(articleTypes).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Bereich
          <select value={spaceId} onChange={(event) => setSpaceId(event.target.value)}>
            <option value="">Kein Bereich</option>
            {spaces.map((space) => (
              <option key={space.id} value={space.id}>
                {space.name}
              </option>
            ))}
          </select>
        </label>
        {details.capabilities.canAdmin && (
          <label>
            Sichtbarkeit
            <select value={visibility} onChange={(event) => setVisibility(event.target.value as Visibility)}>
              {Object.entries(visibilities).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <label>
        Zusammenfassung
        <textarea value={summary} onChange={(event) => setSummary(event.target.value)} rows={2} maxLength={2000} />
      </label>
      <BlockEditor blocks={blocks} onChange={setBlocks} articleId={article.id} />
      <label>
        Änderungsnotiz
        <input value={changeNote} onChange={(event) => setChangeNote(event.target.value)} maxLength={500} placeholder="Was wurde geändert?" />
      </label>
      <div className="row">
        <button type="submit" disabled={saving}>
          Speichern
        </button>
        <button type="button" onClick={onCancel}>
          Abbrechen
        </button>
      </div>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
