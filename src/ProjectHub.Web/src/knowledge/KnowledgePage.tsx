import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { EmptyState } from '../ui/EmptyState'
import { Reveal } from '../ui/Reveal'
import {
  articleStatuses,
  articleTypeEmoji,
  articleTypes,
  createArticle,
  createSpace,
  fetchSpaces,
  fetchTags,
  searchArticles,
  type ArticleFilter,
  type ArticleSummary,
  type Space,
  type Tag,
} from './api'
import { ArticleView } from './ArticleView'
import { typeColors } from './galaxy/graph'
import { KnowledgeGalaxy } from './galaxy/KnowledgeGalaxy'
import { toast } from '../ui/toast'
import { Skeleton } from '../ui/Skeleton'
import { CloseIcon } from '../ui/icons'

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

  /** Like a new page in Notion: the draft exists at once and opens in the editor, where title and type are set. */
  async function newArticle() {
    setError(null)
    try {
      const created = await createArticle({ title: 'Neuer Artikel', articleType: 'article', summary: '', spaceId: filter.spaceId || null, visibility: 'organization' })
      setOpen({ id: created.article.id, editing: true })
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="knowledge" aria-labelledby="knowledge-heading">
      <header className="page-header">
        <h2 id="knowledge-heading">Wissen</h2>
        <button type="submit" onClick={() => void newArticle()}>
          + Artikel
        </button>
      </header>
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
          <SearchBar key={filter.spaceId ?? ''} spaces={spaces} tags={tags} initial={filter} onSearch={setFilter} />
          <div className="project-layout">
            <div>
              {articles === null ? (
                <Skeleton kind="list" count={4} label="Artikel werden geladen" />
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
              <SpacesPanel me={me} spaces={spaces} current={filter.spaceId ?? ''} onFilter={(spaceId) => setFilter({ spaceId })} onCreated={loadFacets} />
              <p className="muted knowledge-note">Neue Artikel sind Entwürfe und nur für dich sichtbar, bis sie veröffentlicht werden.</p>
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

type SearchProps = { spaces: Space[]; tags: Tag[]; initial: ArticleFilter; onSearch: (filter: ArticleFilter) => void }

/**
 * One search field that searches while typing. The filters stay hidden until someone asks for them with the filter
 * button; every active filter shows as a chip that a click removes, like in Linear or Notion.
 */
function SearchBar({ spaces, tags, initial, onSearch }: SearchProps) {
  const [q, setQ] = useState(initial.q ?? '')
  const [type, setType] = useState(initial.type ?? '')
  const [status, setStatus] = useState(initial.status ?? '')
  const [spaceId, setSpaceId] = useState(initial.spaceId ?? '')
  const [tag, setTag] = useState(initial.tag ?? '')
  const [filtersOpen, setFiltersOpen] = useState(false)
  const search = useRef(onSearch)
  useEffect(() => {
    search.current = onSearch
  })

  // Typing searches after a short pause; a changed filter searches at once.
  const first = useRef(true)
  useEffect(() => {
    if (first.current) {
      first.current = false
      return
    }
    const timer = window.setTimeout(() => search.current({ q: q.trim(), type, status, spaceId, tag }), 250)
    return () => window.clearTimeout(timer)
  }, [q, type, status, spaceId, tag])

  const chip = (label: string, value: string, set: (value: string) => void, options: [string, string][]) => (
    <select className={value ? 'filter-chip active' : 'filter-chip'} aria-label={label} value={value} onChange={(event) => set(event.target.value)}>
      <option value="">{label}: alle</option>
      {options.map(([optionValue, optionLabel]) => (
        <option key={optionValue} value={optionValue}>
          {label}: {optionLabel}
        </option>
      ))}
    </select>
  )

  const spaceName = (id: string) => spaces.find((space) => space.id === id)?.name ?? 'unbekannt'
  const active: { label: string; value: string; clear: () => void }[] = [
    ...(type ? [{ label: 'Art', value: articleTypes[type as keyof typeof articleTypes] ?? type, clear: () => setType('') }] : []),
    ...(status ? [{ label: 'Status', value: articleStatuses[status as keyof typeof articleStatuses] ?? status, clear: () => setStatus('') }] : []),
    ...(spaceId ? [{ label: 'Bereich', value: spaceName(spaceId), clear: () => setSpaceId('') }] : []),
    ...(tag ? [{ label: 'Tag', value: tag, clear: () => setTag('') }] : []),
  ]

  function clearAll() {
    setType('')
    setStatus('')
    setSpaceId('')
    setTag('')
  }

  return (
    <form
      className="knowledge-search"
      role="search"
      onSubmit={(event) => {
        event.preventDefault()
        onSearch({ q: q.trim(), type, status, spaceId, tag })
      }}
    >
      <div className="knowledge-search-row">
        <input className="knowledge-search-input" type="search" aria-label="Suche" value={q} onChange={(event) => setQ(event.target.value)} placeholder="Artikel suchen, z. B. Deployment, Kickoff …" />
        <button type="button" className={active.length > 0 ? 'filter-toggle active' : 'filter-toggle'} aria-expanded={filtersOpen} onClick={() => setFiltersOpen(!filtersOpen)}>
          Filter{active.length > 0 && ` (${active.length})`}
        </button>
      </div>
      {filtersOpen && (
        <div className="filter-chips">
          {chip('Art', type, setType, Object.entries(articleTypes))}
          {chip('Status', status, setStatus, Object.entries(articleStatuses))}
          {chip('Bereich', spaceId, setSpaceId, spaces.map((space) => [space.id, space.name]))}
          {chip(
            'Tag',
            tag,
            setTag,
            tags.map((t) => [t.name, `${t.name} (${t.articleCount})`]),
          )}
        </div>
      )}
      {active.length > 0 && (
        <div className="filter-chips" role="group" aria-label="Aktive Filter">
          {active.map((filter) => (
            <button key={filter.label} type="button" className="filter-chip active removable" aria-label={`Filter ${filter.label}: ${filter.value} entfernen`} onClick={filter.clear}>
              {filter.label}: {filter.value} <CloseIcon size={12} />
            </button>
          ))}
          {active.length > 1 && (
            <button type="button" className="link-button" onClick={clearAll}>
              Alle entfernen
            </button>
          )}
        </div>
      )}
    </form>
  )
}

type SpacesProps = { me: Me; spaces: Space[]; current: string; onFilter: (spaceId: string) => void; onCreated: () => void }

function SpacesPanel({ me, spaces, current, onFilter, onCreated }: SpacesProps) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent, close: () => void) {
    event.preventDefault()
    setError(null)
    try {
      await createSpace(name, '')
      toast(`Bereich „${name}“ angelegt.`)
      setName('')
      close()
      onCreated()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel" aria-labelledby="spaces-heading">
      <h3 id="spaces-heading">Bereiche</h3>
      <ul className="plain-list space-list">
        <li className="row">
          <button type="button" className={current === '' ? 'link-button active' : 'link-button'} aria-pressed={current === ''} onClick={() => onFilter('')}>
            Alle Bereiche
          </button>
        </li>
        {spaces.map((space) => (
          <li key={space.id} className="row">
            <button type="button" className={current === space.id ? 'link-button active' : 'link-button'} aria-pressed={current === space.id} onClick={() => onFilter(space.id)}>
              {space.name}
            </button>
            <small className="muted">{space.articleCount} Artikel</small>
          </li>
        ))}
      </ul>
      {me.organizationRole === 'admin' && (
        <Reveal label="Bereich">
          {(close) => (
            <form className="quick-create" onSubmit={(event) => void submit(event, close)}>
              <input aria-label="Neuer Bereich" placeholder="Name, Enter zum Anlegen" value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} autoFocus />
              {error && <p role="alert">{error}</p>}
            </form>
          )}
        </Reveal>
      )}
    </section>
  )
}
