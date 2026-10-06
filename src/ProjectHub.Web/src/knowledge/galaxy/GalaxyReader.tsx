import { useEffect, useRef, useState } from 'react'
import { articleStatuses, articleTypeEmoji, articleTypes, fetchArticle, relationTypes, type ArticleDetails } from '../api'
import { BlockView } from '../BlockView'
import { neighborsOf, typeColors, type GraphNode, type KnowledgeGraph } from './graph'

type Props = {
  graph: KnowledgeGraph
  node: GraphNode
  /** Shows another article of the galaxy in the reader (it becomes the selection). */
  onSelect: (id: string) => void
  onClose: () => void
  /** Opens the full article page (editing, comments, versions). */
  onOpenArticle: (id: string) => void
}

/**
 * The article beside the galaxy: opened by double-clicking a planet or by zooming into it. Read-only; the full page
 * with editing is one click away. Keyed by article, so each article starts fresh.
 */
export function GalaxyReader({ graph, node, onSelect, onClose, onOpenArticle }: Props) {
  const [details, setDetails] = useState<ArticleDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const section = useRef<HTMLElement>(null)

  useEffect(() => {
    let current = true
    fetchArticle(node.id).then(
      (value) => current && setDetails(value),
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [node.id])

  // On phones the side panel sits below the galaxy; bring it into view.
  useEffect(() => {
    if (window.matchMedia?.('(max-width: 48rem)').matches) section.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }, [])

  const neighbors = neighborsOf(graph, node.id)
  const inGalaxy = new Set(graph.nodes.map((n) => n.id))
  const article = details?.article

  return (
    <section ref={section} className="panel galaxy-reader" aria-labelledby="galaxy-reader-heading" style={{ ['--article-type' as string]: typeColors[node.articleType] }}>
      <div className="galaxy-reader-bar">
        <span className="galaxy-reader-type">
          <span aria-hidden="true">{articleTypeEmoji[node.articleType]}</span> {articleTypes[node.articleType]} · {articleStatuses[node.status]}
          {article?.spaceName && ` · ${article.spaceName}`}
        </span>
        <button type="button" className="galaxy-reader-close" aria-label="Artikel schließen" title="Schließen (Esc)" onClick={onClose}>
          ×
        </button>
      </div>
      <h3 id="galaxy-reader-heading">{node.title}</h3>
      {node.summary && <p className="article-summary">{node.summary}</p>}
      {error ? (
        <p role="alert">{error}</p>
      ) : !details ? (
        <p className="muted">Artikel wird geladen …</p>
      ) : (
        <BlockView blocks={details.content.blocks} onOpenArticle={(id) => (inGalaxy.has(id) ? onSelect(id) : onOpenArticle(id))} />
      )}
      {neighbors.length > 0 && (
        <>
          <h4>Verbunden mit</h4>
          <ul className="plain-list galaxy-reader-relations">
            {neighbors.map(({ edge, node: other, direction }) => (
              <li key={edge.id}>
                <small className="muted">{relationTypes[edge.relationType][direction]}</small>{' '}
                <button type="button" className="link-button" onClick={() => onSelect(other.id)}>
                  {other.title}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      <button type="button" onClick={() => onOpenArticle(node.id)}>
        Ganze Seite öffnen
      </button>
    </section>
  )
}
