import { useEffect, useState } from 'react'
import { articleTypeEmoji, searchArticles, type ArticleSummary, type ArticleType, type Space } from './api'
import { OutcomeBadge } from './Insight'
import { kindOf, outcomeOf, toneStyles, type UseCase } from './useCases'
import './insights.css'

type Props = {
  space: Space
  useCase: UseCase
  /** The kind the article list shows, '' for all. */
  type: string
  revision: number
  onType: (type: string) => void
  onCreate: (type: ArticleType) => void
  onOpen: (id: string) => void
}

const monthFormat = new Intl.DateTimeFormat('de-DE', { month: 'short', year: 'numeric' })

/**
 * "Was wissen wir inzwischen?": the insights of a space on a time line, oldest first, each with its result. Above it the kinds
 * of the space's use case filter the list and create new insights (ADR 0023).
 */
export function SpaceInsights({ space, useCase, type, revision, onType, onCreate, onOpen }: Props) {
  const [insights, setInsights] = useState<ArticleSummary[] | null>(null)

  useEffect(() => {
    let current = true
    searchArticles({ spaceId: space.id }).then(
      (page) => {
        if (!current) return
        const kinds = new Set(useCase.kinds.map((k) => k.articleType))
        setInsights(
          page.items
            .filter((a) => kinds.has(a.articleType) && (!type || a.articleType === type))
            .sort((a, b) => (a.createdAt ?? a.updatedAt).localeCompare(b.createdAt ?? b.updatedAt)),
        )
      },
      () => current && setInsights([]),
    )
    return () => {
      current = false
    }
  }, [space.id, useCase, type, revision])

  return (
    <section className="panel space-insights" aria-labelledby="space-insights-heading">
      <div className="space-insights-header">
        <h3 id="space-insights-heading">Was wissen wir inzwischen über {space.name}?</h3>
        <div className="row">
          {useCase.kinds.map((kind) => (
            <button key={kind.articleType} type="button" onClick={() => onCreate(kind.articleType)}>
              + {kind.label}
            </button>
          ))}
        </div>
      </div>
      {useCase.kinds.length > 1 && (
        <div className="filter-chips" role="group" aria-label="Art der Erkenntnis">
          <button type="button" className={type === '' ? 'filter-chip active' : 'filter-chip'} aria-pressed={type === ''} onClick={() => onType('')}>
            Alle
          </button>
          {useCase.kinds.map((kind) => (
            <button
              key={kind.articleType}
              type="button"
              className={type === kind.articleType ? 'filter-chip active' : 'filter-chip'}
              aria-pressed={type === kind.articleType}
              onClick={() => onType(kind.articleType)}
            >
              {kind.pluralLabel}
            </button>
          ))}
        </div>
      )}
      {insights === null ? null : insights.length === 0 ? (
        <p className="muted">Noch keine Erkenntnisse. Leg das erste {useCase.kinds[0]?.label ?? 'Experiment'} an.</p>
      ) : (
        <ol className="insight-timeline" aria-label="Zeitstrahl der Erkenntnisse">
          {insights.map((insight) => {
            const kind = kindOf([useCase], insight.articleType)
            const tone = outcomeOf(kind, insight.outcome)?.tone ?? 'neutral'
            return (
              <li key={insight.id} style={{ ['--tone' as string]: toneStyles[tone].color }}>
                <button type="button" className="insight-step" onClick={() => onOpen(insight.id)}>
                  <span className="muted insight-date">
                    <span aria-hidden="true">{articleTypeEmoji[insight.articleType]}</span> {kind?.label} · {monthFormat.format(new Date(insight.createdAt ?? insight.updatedAt))}
                  </span>
                  <span className="insight-title">{insight.title}</span>
                  <OutcomeBadge kind={kind} outcome={insight.outcome} />
                </button>
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}
