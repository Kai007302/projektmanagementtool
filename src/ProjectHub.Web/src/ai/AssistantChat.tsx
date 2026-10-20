import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { ActionCard, type ActionState } from './ActionCard'
import { activityText, streamChat, type AssistantSource, type ChatContext, type ChatMessage, type Continuation } from './api'
import { AnswerText } from './AnswerText'
import './assistant.css'

type Turn = ChatMessage & { sources?: AssistantSource[]; error?: string; actions?: ActionState[]; continuation?: string }

type Props = {
  onOpenArticle: (id: string) => void
  /** What the questions are about; changing it starts no new conversation, the next question carries it. */
  context: ChatContext
  examples: string[]
  placeholder: string
  /** Disables the controls of the surrounding page while an answer streams. */
  onBusy?: (busy: boolean) => void
  compact?: boolean
}

/** The conversation as the API gets it: what was said, without failed answers. */
const history = (turns: Turn[]): ChatMessage[] => turns.filter((t) => !t.error && t.text).map(({ role, text }) => ({ role, text }))

/**
 * One conversation with the assistant (ADR 0015, 0016): answers stream in as the model writes, with the knowledge articles it
 * used; proposed changes wait for the person's approval. The assistant page and the documentation chat beside the knowledge
 * (ADR 0023) both use it. The conversation lives only here in the browser.
 */
export function AssistantChat({ onOpenArticle, context, examples, placeholder, onBusy, compact = false }: Props) {
  const [turns, setTurns] = useState<Turn[]>([])
  const [question, setQuestion] = useState('')
  const [busy, setBusy] = useState(false)
  const [activity, setActivity] = useState<string | null>(null)
  const abort = useRef<AbortController | null>(null)
  const end = useRef<HTMLDivElement | null>(null)

  useEffect(() => () => abort.current?.abort(), [])
  useEffect(() => end.current?.scrollIntoView?.({ block: 'end' }), [turns, activity])
  useEffect(() => onBusy?.(busy), [busy, onBusy])

  function updateLast(change: (turn: Turn) => Turn) {
    setTurns((all) => (all.length === 0 ? all : [...all.slice(0, -1), change(all[all.length - 1])]))
  }

  /** Streams one answer into the last turn; with a continuation, the turn that waited for approval goes on. */
  async function stream(messages: ChatMessage[], continuation?: Continuation) {
    setBusy(true)
    setActivity(continuation ? 'Führt aus …' : 'Denkt nach …')
    const controller = new AbortController()
    abort.current = controller
    try {
      await streamChat(
        messages,
        context,
        (event) => {
          switch (event.type) {
            case 'delta':
              setActivity(null)
              updateLast((turn) => ({ ...turn, text: turn.text + event.text }))
              break
            case 'tool':
              setActivity(activityText(event.tool))
              break
            case 'sources':
              updateLast((turn) => ({ ...turn, sources: event.sources }))
              break
            case 'approval':
              updateLast((turn) => ({ ...turn, actions: [...(turn.actions ?? []), ...event.actions], continuation: event.continuation }))
              break
            case 'error':
              updateLast((turn) => ({ ...turn, error: event.text }))
              break
          }
        },
        controller.signal,
        continuation,
      )
    } catch (e) {
      if (!controller.signal.aborted) updateLast((turn) => ({ ...turn, error: (e as Error).message }))
    } finally {
      setBusy(false)
      setActivity(null)
      abort.current = null
    }
  }

  async function ask(text: string) {
    const trimmed = text.trim()
    if (!trimmed || busy) return
    const messages: ChatMessage[] = [...history(turns), { role: 'user', text: trimmed }]
    // A new question drops changes that are still waiting: they never run.
    setTurns((all) => [
      ...all.map((turn) =>
        turn.continuation
          ? { ...turn, continuation: undefined, actions: turn.actions?.map((a) => (a.decision === undefined ? { ...a, dropped: true } : a)) }
          : turn,
      ),
      { role: 'user', text: trimmed },
      { role: 'assistant', text: '' },
    ])
    setQuestion('')
    await stream(messages)
  }

  /** Records the person's decision; once every proposed change of the turn is decided, the assistant goes on. */
  async function decide(actionId: string, approved: boolean) {
    const last = turns[turns.length - 1]
    if (busy || !last?.continuation || !last.actions) return
    const actions = last.actions.map((a) => (a.id === actionId ? { ...a, decision: approved } : a))
    const open = actions.filter((a) => !a.dropped && a.decision === undefined)
    if (open.length > 0) {
      updateLast((turn) => ({ ...turn, actions }))
      return
    }

    const continuation: Continuation = {
      continuation: last.continuation,
      approvals: actions.filter((a) => !a.dropped && a.decision !== undefined).map((a) => ({ id: a.id, approved: a.decision! })),
    }
    updateLast((turn) => ({ ...turn, actions, continuation: undefined, text: turn.text && !turn.text.endsWith('\n') ? turn.text + '\n\n' : turn.text }))
    await stream(history(turns.slice(0, -1)), continuation)
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    void ask(question)
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      void ask(question)
    }
  }

  return (
    <div className={compact ? 'assistant-chat compact' : 'assistant-chat'}>
      {turns.length > 0 && (
        <div className="assistant-chat-tools">
          <button
            type="button"
            onClick={() => {
              abort.current?.abort()
              setTurns([])
            }}
          >
            Neues Gespräch
          </button>
        </div>
      )}
      <div className="assistant-log" aria-live="polite" aria-busy={busy}>
        {turns.length === 0 && (
          <div className="assistant-examples">
            {examples.map((example) => (
              <button key={example} type="button" onClick={() => void ask(example)}>
                {example}
              </button>
            ))}
          </div>
        )}
        {turns.map((turn, index) =>
          turn.role === 'user' ? (
            <div key={index} className="assistant-turn assistant-question">
              <p>{turn.text}</p>
            </div>
          ) : (
            <article key={index} className="assistant-turn assistant-answer" aria-label="Antwort des Assistenten">
              {turn.text && <AnswerText text={turn.text} onOpenArticle={onOpenArticle} />}
              {turn.actions?.map((action) => (
                <ActionCard key={action.id} action={action} disabled={busy || !turn.continuation} onDecide={(approved) => void decide(action.id, approved)} />
              ))}
              {index === turns.length - 1 && activity && <p className="muted assistant-activity">{activity}</p>}
              {turn.error && <p role="alert">{turn.error}</p>}
              {turn.sources && turn.sources.length > 0 && (
                <div className="assistant-sources">
                  <h3>Quellen</h3>
                  <ul>
                    {turn.sources.map((source) => (
                      <li key={source.articleId}>
                        <button type="button" className="link-button" onClick={() => onOpenArticle(source.articleId)}>
                          {source.title}
                        </button>
                        {!source.cited && <span className="muted"> (gelesen, nicht zitiert)</span>}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </article>
          ),
        )}
        <div ref={end} />
      </div>

      <form className="assistant-form" onSubmit={submit} aria-label="Frage an den Assistenten">
        <textarea
          aria-label="Deine Frage"
          placeholder={placeholder}
          value={question}
          maxLength={8000}
          rows={2}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={onKeyDown}
        />
        {busy ? (
          <button type="button" onClick={() => abort.current?.abort()}>
            Stopp
          </button>
        ) : (
          <button type="submit" disabled={!question.trim()}>
            Fragen
          </button>
        )}
      </form>
    </div>
  )
}
