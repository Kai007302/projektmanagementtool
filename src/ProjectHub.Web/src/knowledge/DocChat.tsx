import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { AssistantChat } from '../ai/AssistantChat'
import { fetchSpaces, type Space, type UseCaseKey } from './api'
import { CloseIcon } from '../ui/icons'
import './insights.css'

type Scope = 'article' | 'space' | 'all'

type Props = {
  /** The open article and its space; without one the chat asks about a chosen space or everything. */
  article?: { id: string; title: string; spaceId: string | null } | null
  spaceId?: string | null
  onOpenArticle: (id: string) => void
  onClose: () => void
}

const examples: Record<UseCaseKey, string[]> = {
  experiment: ['Welche Ergebnisse widersprechen sich?', 'Was haben wir schon bestätigt?', 'Was ist noch offen?'],
  software: ['Warum haben wir so entschieden?', 'Was lief im letzten Release schief?', 'Was sollte ein neues Teammitglied zuerst lesen?'],
  general: ['Wo steht, wie das geht?', 'Was gilt dafür bei uns?', 'Welche Artikel gibt es dazu?'],
}

/**
 * Questions about the documentation beside the knowledge (ADR 0023): the assistant answers only from articles the person
 * may read, names them, and searches in the chosen space. It runs only when someone asks; nothing is stored.
 */
export function DocChat({ article = null, spaceId = null, onOpenArticle, onClose }: Props) {
  const ownSpace = article?.spaceId ?? spaceId
  const [scope, setScope] = useState<Scope>(article ? 'article' : ownSpace ? 'space' : 'all')
  const [busy, setBusy] = useState(false)
  const [spaces, setSpaces] = useState<Space[]>([])
  const panel = useRef<HTMLElement>(null)
  const space = spaces.find((s) => s.id === ownSpace) ?? null

  useEffect(() => {
    let current = true
    fetchSpaces().then(
      (value) => current && setSpaces(value),
      () => {},
    )
    return () => {
      current = false
    }
  }, [])

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    panel.current?.querySelector<HTMLTextAreaElement>('textarea')?.focus()
    return () => before?.focus?.()
  }, [])

  const context =
    scope === 'article' && article
      ? { articleId: article.id, spaceId: article.spaceId }
      : scope === 'space' && space
        ? { spaceId: space.id }
        : {}

  return createPortal(
    <aside
      ref={panel}
      className="doc-chat"
      aria-labelledby="doc-chat-title"
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <header className="doc-chat-header">
        <h2 id="doc-chat-title">Fragen zur Doku</h2>
        <button type="button" className="icon-button" aria-label="Fragen zur Doku schließen" onClick={onClose}>
          <CloseIcon />
        </button>
      </header>
      <label className="doc-chat-scope">
        Fragt in{' '}
        <select value={scope} onChange={(event) => setScope(event.target.value as Scope)} disabled={busy}>
          {article && <option value="article">diesem Artikel</option>}
          {space && <option value="space">Kategorie {space.name}</option>}
          <option value="all">der ganzen Galaxie</option>
        </select>
      </label>
      <AssistantChat
        compact
        onOpenArticle={onOpenArticle}
        context={context}
        examples={examples[space?.useCase ?? 'general']}
        placeholder="Frage zur Doku stellen …"
        onBusy={setBusy}
      />
      <p className="muted doc-chat-note">Antwortet nur aus Artikeln, die du lesen darfst, mit Quellen. Läuft nur, wenn du fragst; das Gespräch wird nicht gespeichert.</p>
    </aside>,
    document.body,
  )
}
