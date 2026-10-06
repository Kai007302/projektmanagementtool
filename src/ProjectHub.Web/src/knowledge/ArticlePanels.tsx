import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me, User } from '../identity/api'
import { fetchTeams, fetchUsers, type TeamSummary } from '../teams/api'
import { fetchWhiteboards, type Whiteboard } from '../whiteboard/api'
import {
  addKnowledgeComment,
  addReference,
  addRelation,
  deleteKnowledgeComment,
  fetchKnowledgeComments,
  fetchPermissions,
  fetchVersions,
  grants,
  relationTypes,
  removeReference,
  removeRelation,
  resourceTypes,
  restoreVersion,
  setPermissions,
  setTags,
  type ArticleDetails,
  type Grant,
  type KnowledgeComment,
  type Permission,
  type RelationType,
  type ResourceType,
  type VersionSummary,
} from './api'
import { ArticlePicker, ProjectPicker, TaskPicker } from './BlockEditor'
import { EmptyState } from '../ui/EmptyState'
import { Reveal } from '../ui/Reveal'

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

type PanelProps = { details: ArticleDetails; onChanged: () => void }

/** Runs a change and reports failures in the panel. */
function useAction(onChanged: () => void) {
  const [error, setError] = useState<string | null>(null)
  const run = useCallback(
    async (action: () => Promise<unknown>) => {
      setError(null)
      try {
        await action()
        onChanged()
        return true
      } catch (e) {
        setError((e as Error).message)
        return false
      }
    },
    [onChanged],
  )
  return { error, run }
}

export function TagsPanel({ details, onChanged }: PanelProps) {
  const [value, setValue] = useState(details.article.tags.join(', '))
  const { error, run } = useAction(onChanged)

  function submit(event: FormEvent) {
    event.preventDefault()
    const tags = value
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean)
    void run(() => setTags(details.article.id, tags))
  }

  return (
    <section className="panel" aria-labelledby="tags-heading">
      <h3 id="tags-heading">Tags</h3>
      {details.article.tags.length === 0 ? (
        <p className="muted">Keine Tags.</p>
      ) : (
        <p className="tag-list">
          {details.article.tags.map((tag) => (
            <span key={tag} className="tag">
              {tag}
            </span>
          ))}
        </p>
      )}
      {details.capabilities.canEdit && (
        <Reveal label="Tags bearbeiten" plus={false}>
          {() => (
            <form className="stacked-form" onSubmit={submit}>
              <label>
                Tags (mit Komma getrennt)
                <input value={value} onChange={(event) => setValue(event.target.value)} />
              </label>
              <button type="submit">Tags speichern</button>
              {error && <p role="alert">{error}</p>}
            </form>
          )}
        </Reveal>
      )}
    </section>
  )
}

export function RelationsPanel({ details, onChanged, onOpenArticle }: PanelProps & { onOpenArticle: (id: string) => void }) {
  const [target, setTarget] = useState('')
  const [type, setType] = useState<RelationType>('RELATED')
  const { error, run } = useAction(onChanged)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (await run(() => addRelation(details.article.id, target, type))) setTarget('')
  }

  return (
    <section className="panel" aria-labelledby="relations-heading">
      <h3 id="relations-heading">Beziehungen</h3>
      {details.relations.length === 0 ? (
        <p className="muted">Keine Beziehungen.</p>
      ) : (
        <ul className="plain-list">
          {details.relations.map((relation) => (
            <li key={relation.id} className="row">
              <span>
                <small className="muted">{relationTypes[relation.relationType][relation.direction]}</small>{' '}
                <button type="button" className="link-button" onClick={() => onOpenArticle(relation.articleId)}>
                  {relation.title}
                </button>
              </span>
              {details.capabilities.canEdit && relation.direction === 'outgoing' && (
                <button type="button" className="link-button" aria-label={`Beziehung zu ${relation.title} entfernen`} onClick={() => run(() => removeRelation(relation.id))}>
                  Entfernen
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {details.capabilities.canEdit && (
        <Reveal label="Beziehung">
          {() => (
            <form className="stacked-form" onSubmit={submit}>
              <label>
                Beziehung
                <select value={type} onChange={(event) => setType(event.target.value as RelationType)}>
                  {Object.entries(relationTypes).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label.outgoing}
                    </option>
                  ))}
                </select>
              </label>
              <ArticlePicker label="Zielartikel" value={target} onChange={setTarget} exclude={details.article.id} />
              <button type="submit">Beziehung hinzufügen</button>
              {error && <p role="alert">{error}</p>}
            </form>
          )}
        </Reveal>
      )}
    </section>
  )
}

export function ReferencesPanel({ details, onChanged }: PanelProps) {
  const [type, setType] = useState<ResourceType>('project')
  const [resourceId, setResourceId] = useState('')
  const { error, run } = useAction(onChanged)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (await run(() => addReference(details.article.id, type, resourceId))) setResourceId('')
  }

  return (
    <section className="panel" aria-labelledby="references-heading">
      <h3 id="references-heading">Verknüpft mit</h3>
      {details.references.length === 0 ? (
        <p className="muted">Keine Verknüpfungen.</p>
      ) : (
        <ul className="plain-list">
          {details.references.map((reference) => (
            <li key={reference.id} className="row">
              <span>
                <small className="muted">{resourceTypes[reference.resourceType]}</small> {reference.title}
              </span>
              {details.capabilities.canEdit && (
                <button type="button" className="link-button" aria-label={`Verknüpfung mit ${reference.title} entfernen`} onClick={() => run(() => removeReference(reference.id))}>
                  Entfernen
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {details.capabilities.canEdit && (
        <Reveal label="Verknüpfung">
          {() => (
            <form className="stacked-form" onSubmit={submit}>
              <label>
                Art
                <select
                  value={type}
                  onChange={(event) => {
                    setType(event.target.value as ResourceType)
                    setResourceId('')
                  }}
                >
                  {Object.entries(resourceTypes).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              {type === 'project' && <ProjectPicker label="Projekt" value={resourceId} onChange={setResourceId} />}
              {type === 'task' && <TaskPicker label="Verknüpfung" value={resourceId} onChange={setResourceId} />}
              {type === 'team' && <TeamPicker value={resourceId} onChange={setResourceId} />}
              {type === 'whiteboard' && <WhiteboardPicker value={resourceId} onChange={setResourceId} />}
              <button type="submit">Verknüpfen</button>
              {error && <p role="alert">{error}</p>}
            </form>
          )}
        </Reveal>
      )}
    </section>
  )
}

/** Picks a project first, then one of its whiteboards. */
function WhiteboardPicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const [projectId, setProjectId] = useState('')
  const [boards, setBoards] = useState<Whiteboard[]>([])
  useEffect(() => {
    if (!projectId) return
    fetchWhiteboards(projectId).then((page) => setBoards(page.items), () => setBoards([]))
  }, [projectId])
  return (
    <div className="block-fields">
      <ProjectPicker label="Whiteboard: Projekt" value={projectId} onChange={setProjectId} required={false} />
      <label className="grow">
        Whiteboard
        <select value={value} onChange={(event) => onChange(event.target.value)} required disabled={!projectId}>
          <option value="">Whiteboard wählen …</option>
          {boards.map((board) => (
            <option key={board.id} value={board.id}>
              {board.name}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}

function TeamPicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const [teams, setTeams] = useState<TeamSummary[]>([])
  useEffect(() => {
    fetchTeams().then((page) => setTeams(page.items), () => setTeams([]))
  }, [])
  return (
    <label className="block-fields stacked">
      Team
      <select value={value} onChange={(event) => onChange(event.target.value)} required>
        <option value="">Team wählen …</option>
        {teams.map((team) => (
          <option key={team.id} value={team.id}>
            {team.name}
          </option>
        ))}
      </select>
    </label>
  )
}

export function VersionsPanel({ details, onChanged, revision }: PanelProps & { revision: number }) {
  const [versions, setVersions] = useState<VersionSummary[]>([])
  const { error, run } = useAction(onChanged)

  useEffect(() => {
    fetchVersions(details.article.id).then(setVersions, () => setVersions([]))
  }, [details.article.id, revision])

  function restore(number: number) {
    if (!window.confirm(`Version ${number} wiederherstellen? Sie wird als neue Version gespeichert.`)) return
    void run(() => restoreVersion(details.article.id, number, details.article.version))
  }

  return (
    <section className="panel" aria-labelledby="versions-heading">
      <h3 id="versions-heading">Versionen</h3>
      {error && <p role="alert">{error}</p>}
      <ol className="plain-list" reversed>
        {versions.map((version) => (
          <li key={version.id} className="row">
            <span>
              <strong>Version {version.versionNumber}</strong>
              {version.versionNumber === details.versionNumber && ' (aktuell)'}
              <br />
              <small className="muted">
                {version.createdByName ?? 'Unbekannt'} · {dateFormat.format(new Date(version.createdAt))}
                {version.changeNote && ` · ${version.changeNote}`}
              </small>
            </span>
            {details.capabilities.canEdit && version.versionNumber !== details.versionNumber && (
              <button type="button" className="link-button" aria-label={`Version ${version.versionNumber} wiederherstellen`} onClick={() => restore(version.versionNumber)}>
                Wiederherstellen
              </button>
            )}
          </li>
        ))}
      </ol>
    </section>
  )
}

type Principal = { key: string; type: Permission['principalType']; id: string; name: string }

export function PermissionsPanel({ details, onChanged }: PanelProps) {
  const [permissions, setPermissionsState] = useState<Permission[]>([])
  const [principals, setPrincipals] = useState<Principal[]>([])
  const [principal, setPrincipal] = useState('')
  const [grant, setGrant] = useState<Grant>('view')
  const { error, run } = useAction(onChanged)
  const articleId = details.article.id

  const load = useCallback(() => {
    fetchPermissions(articleId).then(setPermissionsState, () => setPermissionsState([]))
  }, [articleId])

  useEffect(load, [load])

  useEffect(() => {
    Promise.all([fetchUsers(), fetchTeams()]).then(
      ([users, teams]) =>
        setPrincipals([
          ...users.items.map((u: User) => ({ key: `user:${u.id}`, type: 'user' as const, id: u.id, name: u.displayName })),
          ...teams.items.map((t) => ({ key: `team:${t.id}`, type: 'team' as const, id: t.id, name: `Team ${t.name}` })),
        ]),
      () => setPrincipals([]),
    )
  }, [])

  function save(next: Permission[]) {
    void run(async () => {
      setPermissionsState(await setPermissions(articleId, next.map(({ principalType, principalId, permission }) => ({ principalType, principalId, permission }))))
    })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const chosen = principals.find((p) => p.key === principal)
    if (!chosen) return
    const others = permissions.filter((p) => !(p.principalType === chosen.type && p.principalId === chosen.id))
    save([...others, { principalType: chosen.type, principalId: chosen.id, name: chosen.name, permission: grant }])
    setPrincipal('')
  }

  return (
    <section className="panel" aria-labelledby="permissions-heading">
      <h3 id="permissions-heading">Freigaben</h3>
      <p className="muted">
        {details.article.visibility === 'organization'
          ? 'Veröffentlicht lesen alle in der Organisation. Freigaben geben zusätzliche Rechte, auch für Entwürfe.'
          : 'Eingeschränkt: Nur die folgenden Personen und Teams sehen den Artikel.'}
      </p>
      {permissions.length === 0 ? (
        <p className="muted">Keine Freigaben.</p>
      ) : (
        <ul className="plain-list">
          {permissions.map((p) => (
            <li key={`${p.principalType}:${p.principalId}`} className="row">
              <span>
                {p.principalType === 'team' ? `Team ${p.name}` : p.name} · {grants[p.permission]}
              </span>
              <button
                type="button"
                className="link-button"
                aria-label={`Freigabe für ${p.name} entfernen`}
                onClick={() => save(permissions.filter((other) => other !== p))}
              >
                Entfernen
              </button>
            </li>
          ))}
        </ul>
      )}
      <Reveal label="Freigabe">
        {() => (
          <form className="stacked-form" onSubmit={submit}>
            <label>
              Person oder Team
              <select value={principal} onChange={(event) => setPrincipal(event.target.value)} required>
                <option value="">Wählen …</option>
                {principals.map((p) => (
                  <option key={p.key} value={p.key}>
                    {p.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Recht
              <select value={grant} onChange={(event) => setGrant(event.target.value as Grant)}>
                {Object.entries(grants).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit">Freigeben</button>
            {error && <p role="alert">{error}</p>}
          </form>
        )}
      </Reveal>
    </section>
  )
}

/** Finds "@Display Name" of people in a comment; the API checks they can read the article. */
function mentionedIn(content: string, users: User[]): string[] {
  return users.filter((user) => content.includes(`@${user.displayName}`)).map((user) => user.id)
}

export function ArticleComments({ articleId, me, canModerate }: { articleId: string; me: Me; canModerate: boolean }) {
  const [comments, setComments] = useState<KnowledgeComment[]>([])
  const [users, setUsers] = useState<User[]>([])
  const [content, setContent] = useState('')

  const load = useCallback(() => {
    fetchKnowledgeComments(articleId).then((page) => setComments(page.items), () => setComments([]))
  }, [articleId])
  const { error, run } = useAction(load)

  useEffect(load, [load])

  useEffect(() => {
    fetchUsers().then((page) => setUsers(page.items), () => setUsers([]))
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (await run(() => addKnowledgeComment(articleId, content, mentionedIn(content, users)))) setContent('')
  }

  return (
    <section className="panel article-comments" aria-labelledby="article-comments-heading">
      <h3 id="article-comments-heading">Kommentare</h3>
      {error && <p role="alert">{error}</p>}
      {comments.length === 0 && <EmptyState emoji="💬">Noch keine Kommentare.</EmptyState>}
      <ul className="plain-list">
        {comments.map((comment) => (
          <li key={comment.id} className="comment">
            <div className="row">
              <span>
                <strong>{comment.authorName}</strong> <small className="muted">{dateFormat.format(new Date(comment.createdAt))}</small>
              </span>
              {(comment.authorId === me.id || canModerate) && (
                <button
                  type="button"
                  className="link-button"
                  aria-label={`Kommentar von ${comment.authorName} löschen`}
                  onClick={() => run(() => deleteKnowledgeComment(comment.id))}
                >
                  Löschen
                </button>
              )}
            </div>
            <p>{comment.content}</p>
          </li>
        ))}
      </ul>
      <form className="stacked-form" onSubmit={submit}>
        <label>
          Kommentar
          <textarea value={content} onChange={(event) => setContent(event.target.value)} required maxLength={10000} rows={3} placeholder="@Name erwähnt Personen, die den Artikel lesen dürfen" />
        </label>
        <button type="submit">Kommentieren</button>
      </form>
    </section>
  )
}
