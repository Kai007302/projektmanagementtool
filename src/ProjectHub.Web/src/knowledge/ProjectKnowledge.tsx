import { useEffect, useState } from 'react'
import { articleTypeEmoji, articleTypes, fetchLinkedKnowledge, type ArticleSummary } from './api'
import { EmptyState } from '../ui/EmptyState'

type Props = { projectId: string; revision: number; onOpenArticle?: (id: string) => void }

/** Knowledge linked to a project: what the team should know while working on it. */
export function ProjectKnowledge({ projectId, revision, onOpenArticle }: Props) {
  const [articles, setArticles] = useState<ArticleSummary[] | null>(null)

  useEffect(() => {
    fetchLinkedKnowledge('project', projectId).then(setArticles, () => setArticles([]))
  }, [projectId, revision])

  return (
    <section className="panel" aria-labelledby="project-knowledge-heading">
      <h3 id="project-knowledge-heading">Wissen</h3>
      {articles === null ? (
        <p>Wird geladen …</p>
      ) : articles.length === 0 ? (
        <EmptyState emoji="📚">Noch keine Artikel mit diesem Projekt verknüpft.</EmptyState>
      ) : (
        <ul className="plain-list">
          {articles.map((article) => (
            <li key={article.id}>
              {onOpenArticle ? (
                <button type="button" className="link-button" onClick={() => onOpenArticle(article.id)}>
                  {article.title}
                </button>
              ) : (
                article.title
              )}{' '}
              <small className="muted">
                <span aria-hidden="true">{articleTypeEmoji[article.articleType]}</span> {articleTypes[article.articleType]}
              </small>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
