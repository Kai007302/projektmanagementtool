import { useEffect, useState } from 'react'
import { articleTypes, fetchLinkedKnowledge, type ArticleSummary } from './api'

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
        <p className="muted">Noch keine Artikel mit diesem Projekt verknüpft.</p>
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
              <small className="muted">{articleTypes[article.articleType]}</small>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
