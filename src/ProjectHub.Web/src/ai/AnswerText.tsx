import { Fragment, type ReactNode } from 'react'

const articleLink = /^article:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i
const inlinePattern = /\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)/g

type Props = { text: string; onOpenArticle: (id: string) => void }

/**
 * Renders the small Markdown subset the assistant writes (paragraphs, lists, bold, code, links). Only links to
 * ProjectHub articles become clickable; any other link stays text and no image is ever loaded, so content the
 * model read can not make the browser contact other servers (prompt injection, ADR 0015).
 */
export function AnswerText({ text, onOpenArticle }: Props) {
  function inline(line: string, keyPrefix: string): ReactNode[] {
    const nodes: ReactNode[] = []
    let last = 0
    for (const match of line.matchAll(inlinePattern)) {
      if (match.index > last) nodes.push(line.slice(last, match.index))
      const key = `${keyPrefix}-${match.index}`
      const [, bold, code, label, href] = match
      if (bold !== undefined) nodes.push(<strong key={key}>{bold}</strong>)
      else if (code !== undefined) nodes.push(<code key={key}>{code}</code>)
      else {
        const article = articleLink.exec(href)
        nodes.push(
          article ? (
            <button key={key} type="button" className="link-button" onClick={() => onOpenArticle(article[1])}>
              {label}
            </button>
          ) : (
            <Fragment key={key}>{label}</Fragment>
          ),
        )
      }
      last = match.index + match[0].length
    }
    if (last < line.length) nodes.push(line.slice(last))
    return nodes
  }

  const blocks = text.trim().split(/\n{2,}/)
  return (
    <div className="answer-text">
      {blocks.map((block, index) => {
        const lines = block.split('\n')
        if (lines.every((line) => /^\s*[-*] /.test(line))) {
          return (
            <ul key={index}>
              {lines.map((line, i) => (
                <li key={i}>{inline(line.replace(/^\s*[-*] /, ''), `${index}-${i}`)}</li>
              ))}
            </ul>
          )
        }
        if (lines.every((line) => /^\s*\d+\. /.test(line))) {
          return (
            <ol key={index}>
              {lines.map((line, i) => (
                <li key={i}>{inline(line.replace(/^\s*\d+\. /, ''), `${index}-${i}`)}</li>
              ))}
            </ol>
          )
        }
        return (
          <p key={index}>
            {lines.map((line, i) => (
              <Fragment key={i}>
                {i > 0 && <br />}
                {/^#{1,6} /.test(line) ? <strong>{inline(line.replace(/^#{1,6} /, ''), `${index}-${i}`)}</strong> : inline(line, `${index}-${i}`)}
              </Fragment>
            ))}
          </p>
        )
      })}
    </div>
  )
}
