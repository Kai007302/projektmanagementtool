import { useEffect, useMemo, useRef, useState } from 'react'
import { searchArticles, type ArticleSummary } from '../knowledge/api'
import { fetchProjects, type ProjectSummary } from '../projects/api'

export type Destination = { label: string; go: () => void }

type Props = {
  /** Places to jump to, e.g. the main areas. */
  destinations: Destination[]
  onOpenProject: (id: string) => void
  onOpenArticle: (id: string) => void
  onClose: () => void
}

type Item = { id: string; group: string; label: string; hint?: string; run: () => void }

const normalize = (text: string) => text.toLocaleLowerCase('de-DE')

/**
 * Linear's Ctrl+K: one field that finds areas, projects and knowledge articles and jumps there. Arrow keys choose,
 * Enter opens, Escape closes.
 */
export function CommandPalette({ destinations, onOpenProject, onOpenArticle, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [projects, setProjects] = useState<ProjectSummary[]>([])
  const [articles, setArticles] = useState<ArticleSummary[]>([])
  const [active, setActive] = useState(0)
  const list = useRef<HTMLUListElement>(null)

  useEffect(() => {
    fetchProjects().then((page) => setProjects(page.items), () => setProjects([]))
  }, [])

  // Articles come from the search API, a moment after typing stops.
  const term = query.trim()
  useEffect(() => {
    if (term.length < 2) return
    let current = true
    const timer = window.setTimeout(() => {
      searchArticles({ q: term }).then(
        (page) => current && setArticles(page.items.slice(0, 6)),
        () => current && setArticles([]),
      )
    }, 250)
    return () => {
      current = false
      window.clearTimeout(timer)
    }
  }, [term])

  const items = useMemo<Item[]>(() => {
    const q = normalize(term)
    const matches = (text: string) => normalize(text).includes(q)
    const close = (action: () => void) => () => {
      onClose()
      action()
    }
    return [
      ...destinations.filter((d) => matches(d.label)).map((d) => ({ id: `nav-${d.label}`, group: 'Bereiche', label: d.label, run: close(d.go) })),
      ...projects
        .filter((p) => matches(p.name))
        .slice(0, 8)
        .map((p) => ({ id: `project-${p.id}`, group: 'Projekte', label: p.name, run: close(() => onOpenProject(p.id)) })),
      ...(term.length < 2
        ? []
        : articles.map((a) => ({ id: `article-${a.id}`, group: 'Wissen', label: a.title, hint: a.summary ?? undefined, run: close(() => onOpenArticle(a.id)) }))),
    ]
  }, [term, destinations, projects, articles, onClose, onOpenProject, onOpenArticle])

  const selected = Math.min(active, Math.max(items.length - 1, 0))

  useEffect(() => {
    list.current?.querySelector(`[data-index="${selected}"]`)?.scrollIntoView?.({ block: 'nearest' })
  }, [selected])

  return (
    <div className="palette-backdrop" onPointerDown={(event) => event.target === event.currentTarget && onClose()}>
      <div className="palette" role="dialog" aria-modal="true" aria-label="Suchen und springen">
        <input
          className="palette-input"
          role="combobox"
          aria-expanded="true"
          aria-controls="palette-list"
          aria-activedescendant={items[selected] ? `palette-${items[selected].id}` : undefined}
          aria-label="Suchen oder springen"
          placeholder="Projekt, Artikel oder Bereich suchen …"
          value={query}
          autoFocus
          onChange={(event) => {
            setQuery(event.target.value)
            setActive(0)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault()
              onClose()
            } else if (event.key === 'ArrowDown') {
              event.preventDefault()
              setActive(Math.min(selected + 1, items.length - 1))
            } else if (event.key === 'ArrowUp') {
              event.preventDefault()
              setActive(Math.max(selected - 1, 0))
            } else if (event.key === 'Enter') {
              event.preventDefault()
              items[selected]?.run()
            }
          }}
        />
        <ul ref={list} id="palette-list" className="palette-list" role="listbox" aria-label="Treffer">
          {items.length === 0 && <li className="palette-empty muted">Nichts gefunden.</li>}
          {items.map((item, index) => (
            <li
              key={item.id}
              id={`palette-${item.id}`}
              data-index={index}
              role="option"
              aria-selected={index === selected}
              className={index === selected ? 'palette-item active' : 'palette-item'}
              onPointerMove={() => setActive(index)}
              onClick={item.run}
            >
              <span className="palette-group">{item.group}</span>
              <span className="palette-label">{item.label}</span>
              {item.hint && <span className="palette-hint muted">{item.hint}</span>}
            </li>
          ))}
        </ul>
        <p className="palette-footer muted">
          <kbd>↑</kbd> <kbd>↓</kbd> wählen · <kbd>Enter</kbd> öffnen · <kbd>Esc</kbd> schließen
        </p>
      </div>
    </div>
  )
}
