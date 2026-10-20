import { useEffect, useState } from 'react'
import { fetchProject } from '../projects/api'
import { fetchTask } from '../tasks/api'
import { fetchArticle } from './api'
import { calloutTones, safeUrl, type Block, type InsightBlockField } from './blocks'
import { DataTableView } from './DataTableView'
import type { InsightKind } from './useCases'
import './insights.css'

/** kind: the insight kind of the article, it marks the field that carries the result. */
type Props = { blocks: Block[]; onOpenArticle?: (id: string) => void; kind?: InsightKind | null }

/** Renders article content. Blocks are plain text; React escapes everything. */
export function BlockView({ blocks, onOpenArticle, kind = null }: Props) {
  if (blocks.length === 0) return <p className="muted">Noch kein Inhalt.</p>
  return (
    <div className="article-content">
      {blocks.map((block, index) => (
        <BlockItem key={block.id || index} block={block} onOpenArticle={onOpenArticle} kind={kind} />
      ))}
    </div>
  )
}

/** The card of an insight: its fields side by side, the result field highlighted. Empty fields show as a hint. */
export function InsightCard({ fields, kind }: { fields: InsightBlockField[]; kind: InsightKind | null }) {
  const highlight = kind?.fields.find((f) => f.highlight)?.key ?? fields[fields.length - 1]?.key
  if (fields.length === 0) return null
  return (
    <dl className="insight-card">
      {fields.map((field) => (
        <div key={field.key} className={field.key === highlight ? 'insight-field highlight' : 'insight-field'}>
          <dt>{field.label}</dt>
          <dd>{field.text.trim() ? field.text : <span className="muted">Noch offen</span>}</dd>
        </div>
      ))}
    </dl>
  )
}

function BlockItem({ block, onOpenArticle, kind }: { block: Block; onOpenArticle?: (id: string) => void; kind: InsightKind | null }) {
  switch (block.type) {
    case 'heading': {
      const Heading = (['h3', 'h4', 'h5'] as const)[block.level - 1]
      return <Heading>{block.text}</Heading>
    }
    case 'paragraph':
      return <p>{block.text}</p>
    case 'quote':
      return <blockquote>{block.text}</blockquote>
    case 'callout':
      return (
        <aside className={`callout callout-${block.tone}`} aria-label={calloutTones[block.tone]}>
          {block.text}
        </aside>
      )
    case 'code':
      return (
        <pre className="code-block" aria-label={block.language ? `Code (${block.language})` : 'Code'}>
          <code>{block.code}</code>
        </pre>
      )
    case 'bullet_list':
      return (
        <ul>
          {block.items.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ul>
      )
    case 'numbered_list':
      return (
        <ol>
          {block.items.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ol>
      )
    case 'checklist':
      return (
        <ul className="checklist">
          {block.items.map((item, index) => (
            <li key={index}>
              <span aria-hidden="true">{item.checked ? '☑' : '☐'}</span> {item.text}
              {item.checked && <span className="visually-hidden"> (erledigt)</span>}
            </li>
          ))}
        </ul>
      )
    case 'image':
      return safeUrl(block.url) ? <img className="article-image" src={block.url} alt={block.alt ?? ''} /> : null
    case 'file':
    case 'link': {
      const label = (block.type === 'file' ? block.name : block.label) || block.url
      return safeUrl(block.url) ? (
        <p>
          <a href={block.url} target="_blank" rel="noreferrer noopener">
            {block.type === 'file' ? `📎 ${label}` : label}
          </a>
        </p>
      ) : null
    }
    case 'task_reference':
      return <ResolvedReference kind="Aufgabe" id={block.taskId} resolve={taskTitle} />
    case 'project_reference':
      return <ResolvedReference kind="Projekt" id={block.projectId} resolve={projectTitle} />
    case 'insight':
      return <InsightCard fields={block.fields} kind={kind} />
    case 'data_table':
      return <DataTableView title={block.title} columns={block.columns} rows={block.rows} chart={block.chart} />
    case 'knowledge_reference':
      return (
        <ResolvedReference
          kind="Wissensartikel"
          id={block.articleId}
          resolve={articleTitle}
          onOpen={onOpenArticle && (() => onOpenArticle(block.articleId))}
        />
      )
  }
}

const taskTitle = (id: string) => fetchTask(id).then((task) => task.title)
const projectTitle = (id: string) => fetchProject(id).then((project) => project.name)
const articleTitle = (id: string) => fetchArticle(id).then((details) => details.article.title)

type ReferenceProps = { kind: string; id: string; resolve: (id: string) => Promise<string>; onOpen?: () => void }

/** Shows the title of a referenced item, or that the reader cannot see it. */
function ResolvedReference({ kind, id, resolve, onOpen }: ReferenceProps) {
  const [title, setTitle] = useState<string | null | undefined>(undefined)

  useEffect(() => {
    let current = true
    resolve(id).then(
      (value) => current && setTitle(value),
      () => current && setTitle(null),
    )
    return () => {
      current = false
    }
  }, [id, resolve])

  return (
    <p className="reference-block">
      <span className="reference-kind">{kind}</span>{' '}
      {title === undefined ? (
        '…'
      ) : title === null ? (
        <em>nicht verfügbar</em>
      ) : onOpen ? (
        <button type="button" className="link-button" onClick={onOpen}>
          {title}
        </button>
      ) : (
        <strong>{title}</strong>
      )}
    </p>
  )
}
