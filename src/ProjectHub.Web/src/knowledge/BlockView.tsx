import { useEffect, useState } from 'react'
import { fetchProject } from '../projects/api'
import { fetchTask } from '../tasks/api'
import { fetchArticle } from './api'
import { calloutTones, safeUrl, type Block } from './blocks'

type Props = { blocks: Block[]; onOpenArticle?: (id: string) => void }

/** Renders article content. Blocks are plain text; React escapes everything. */
export function BlockView({ blocks, onOpenArticle }: Props) {
  if (blocks.length === 0) return <p className="muted">Noch kein Inhalt.</p>
  return (
    <div className="article-content">
      {blocks.map((block, index) => (
        <BlockItem key={block.id || index} block={block} onOpenArticle={onOpenArticle} />
      ))}
    </div>
  )
}

function BlockItem({ block, onOpenArticle }: { block: Block; onOpenArticle?: (id: string) => void }) {
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
