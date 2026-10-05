import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { EmptyState } from '../ui/EmptyState'
import {
  articleStatuses,
  articleTypeEmoji,
  articleTypes,
  createArticle,
  createSpace,
  fetchSpaces,
  fetchTags,
  searchArticles,
  visibilities,
  type ArticleFilter,
  type ArticleSummary,
  type ArticleType,
  type Space,
  type Tag,
  type Visibility,
} from './api'
import { ArticleView } from './ArticleView'
import { typeColors } from './galaxy/graph'
import { KnowledgeGalaxy } from './galaxy/KnowledgeGalaxy'

type Props = { me: Me; initialArticleId?: string | null }

type Open = { id: string; editing: boolean }

type Mode = 'articles' | 'galaxy'

export function KnowledgePage({ me, initialArticleId = null }: Props) {
  const [open, setOpen] = useState<Open | null>(initialArticleId ? { id: initialArticleId, editing: false } : null)
  const [filter, setFilter] = useState<ArticleFilter>({})
  const [articles, setArticles] = useState<ArticleSummary[] | null>(null)
  const [spaces, setSpaces] = useState<Space[]>([])
  const [tags, setTags] = useState<Tag[]>([])
  const [error, setError] = useState<string | null>(null)
  const [mode, setMode] = useState<Mode>('articles')

  const load = useCallback(() => {
    searchArticles(filter).then(
      (page) => setArticles(page.items),
      (e: Error) => setError(e.message),
    )
  }, [filter])

  const loadFacets = useCallback(() => {
    fetchSpaces().then(setSpaces, () => setSpaces([]))
    fetchTags().then(setTags, () => setTags([]))
  }, [])

  useEffect(load, [load])
  useEffect(loadFacets, [loadFacets])

  const changed = useCallback(() => {
    load()
    loadFacets()
  }, [load, loadFacets])

  const openArticle = useCallback((id: string) => setOpen({ id, editing: false }), [])

  if (open) {
    return (
      <ArticleView
        key={open.id}
        articleId={open.id}
        me={me}
        startEditing={open.editing}
        onBack={() => setOpen(null)}
        onOpenArticle={openArticle}
        onChanged={changed}
      />
    )
  }

  return (
    <section className="knowledge" aria-labelledby="knowledge-heading">
      <h2 id="knowledge-heading">Wissen</h2>
      {error && <p role="alert">{error}</p>}
      <nav className="tabs" aria-label="Wissen anzeigen als">
        {(['articles', 'galaxy'] as Mode[]).map((value) => (
          <button
            key={value}
            type="button"
            className={value === mode ? 'tab active' : 'tab'}
            aria-current={value === mode ? 'page' : undefined}
            onClick={() => setMode(value)}
          >
            {value === 'articles' ? 'Artikel' : 'Galaxie'}
          </button>
        ))}
      </nav>
      {mode === 'galaxy' ? (
        <KnowledgeGalaxy spaces={spaces} onOpenArticle={openArticle} />
      ) : (
        <>
          <SearchForm spaces={spaces} tags={tags} onSearch={setFilter} />
          <div className="project-layout">
            <div>
              {articles === null ? (
                <p>Artikel werden geladen …</p>
              ) : articles.length === 0 ? (
                <EmptyState emoji="🔍" hint="Probier einen anderen Suchbegriff oder Filter.">
                  Keine Artikel gefunden.
                </EmptyState>
              ) : (
                <ul className="card-list" aria-label="Artikel">
                  {articles.map((article) => (
                    <li key={article.id}>
                      <ArticleCard article={article} onOpen={() => openArticle(article.id)} />
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <aside className="project-side">
              <CreateArticleForm spaces={spaces} onCreated={(id) => setOpen({ id, editing: true })} />
              <SpacesPanel me={me} spaces={spaces} onFilter={(spaceId) => setFilter({ spaceId })} onCreated={loadFacets} />
            </aside>
          </div>
        </>
      )}
    </section>
  )
}

export function ArticleCard({ article, onOpen }: { article: ArticleSummary; onOpen: () => void }) {
  return (
    <button
      type="button"
      className="card article-card"
      style={{ ['--article-type' as string]: typeColors[article.articleType] }}
      onClick={onOpen}
    >
      <span className="card-title">
        <span className="article-type-icon" aria-hidden="true">
          {articleTypeEmoji[article.articleType]}
        </span>{' '}
        {article.title}
        {article.visibility === 'restricted' && <span aria-label="eingeschränkt"> 🔒</span>}
      </span>
      <span className="card-meta">
        {articleTypes[article.articleType]} · <span className={`article-status status-${article.status}`}>{articleStatuses[article.status]}</span>
        {article.spaceName && ` · ${article.spaceName}`}
      </span>
      {article.summary && <span className="article-card-summary">{article.summary}</span>}
      {article.tags.length > 0 && (
        <span className="tag-list">
          {article.tags.map((tag) => (
            <span key={tag} className="tag">
              {tag}
            </span>
          ))}
        </span>
      )}
    </button>
  )
}

function SearchForm({ spaces, tags, onSearch }: { spaces: Space[]; tags: Tag[]; onSearch: (filter: ArticleFilter) => void }) {
  const [q, setQ] = useState('')
  const [type, setType] = useState('')
  const [status, setStatus] = useState('')
  const [spaceId, setSpaceId] = useState('')
  const [tag, setTag] = useState('')

  function submit(event: FormEvent) {
    event.preventDefault()
    onSearch({ q: q.trim(), type, status, spaceId, tag })
  }

  return (
    <form className="inline-form knowledge-search" role="search" onSubmit={submit}>
      <label className="grow">
        Suche
        <input type="search" value={q} onChange={(event) => setQ(event.target.value)} placeholder="z. B. Deployment, Kickoff …" />
      </label>
      <label>
        Art
        <select value={type} onChange={(event) => setType(event.target.value)}>
          <option value="">Alle</option>
          {Object.entries(articleTypes).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Status
        <select value={status} onChange={(event) => setStatus(event.target.value)}>
          <option value="">Alle</option>
          {Object.entries(articleStatuses).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Bereich
        <select value={spaceId} onChange={(event) => setSpaceId(event.target.value)}>
          <option value="">Alle</option>
          {spaces.map((space) => (
            <option key={space.id} value={space.id}>
              {space.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Tag
        <select value={tag} onChange={(event) => setTag(event.target.value)}>
          <option value="">Alle</option>
          {tags.map((t) => (
            <option key={t.name} value={t.name}>
              {t.name} ({t.articleCount})
            </option>
          ))}
        </select>
      </label>
      <button type="submit">Suchen</button>
    </form>
  )
}

function CreateArticleForm({ spaces, onCreated }: { spaces: Space[]; onCreated: (id: string) => void }) {
  const [title, setTitle] = useState('')
  const [articleType, setArticleType] = useState<ArticleType>('article')
  const [spaceId, setSpaceId] = useState('')
  const [visibility, setVisibility] = useState<Visibility>('organization')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const created = await createArticle({ title, articleType, summary: '', spaceId: spaceId || null, visibility })
      onCreated(created.article.id)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel" aria-labelledby="new-article-heading">
      <h3 id="new-article-heading">Neuer Artikel</h3>
      <form className="stacked-form" onSubmit={submit}>
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
        <p className="muted">Neue Artikel sind Entwürfe und nur für dich sichtbar, bis sie veröffentlicht werden.</p>
        <button type="submit">Artikel anlegen</button>
        {error && <p role="alert">{error}</p>}
      </form>
    </section>
  )
}

function SpacesPanel({ me, spaces, onFilter, onCreated }: { me: Me; spaces: Space[]; onFilter: (spaceId: string) => void; onCreated: () => void }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await createSpace(name, '')
      setName('')
      onCreated()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel" aria-labelledby="spaces-heading">
      <h3 id="spaces-heading">Bereiche</h3>
      <ul className="plain-list">
        {spaces.map((space) => (
          <li key={space.id} className="row">
            <button type="button" className="link-button" onClick={() => onFilter(space.id)}>
              {space.name}
            </button>
            <small className="muted">{space.articleCount} Artikel</small>
          </li>
        ))}
      </ul>
      {me.organizationRole === 'admin' && (
        <form className="inline-form" onSubmit={submit}>
          <label>
            Neuer Bereich
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
          </label>
          <button type="submit">Anlegen</button>
          {error && <p role="alert">{error}</p>}
        </form>
      )}
    </section>
  )
}
