import { useEffect, useState } from 'react'
import { fetchProjects, type ProjectSummary } from '../projects/api'
import { AssistantChat } from './AssistantChat'
import type { AiStatus } from './api'
import './assistant.css'

type Props = { status: AiStatus; onOpenArticle: (id: string) => void }

const examples = ['Wie läuft ein Projekt-Kickoff ab?', 'Welche Aufgaben sind mir zugewiesen?', 'Wen rufe ich bei einer Störung an?']
const actionExample = 'Leg mir eine Aufgabe „Protokoll schreiben“ mit Fälligkeit morgen an.'

/**
 * The assistant (ADR 0015, 0016): questions about projects, tasks and knowledge, answered from what the person may see,
 * streamed as the model writes, with the knowledge articles it used. Changes it proposes wait for the person's approval.
 */
export function AssistantPage({ status, onOpenArticle }: Props) {
  const [projects, setProjects] = useState<ProjectSummary[]>([])
  const [projectId, setProjectId] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let current = true
    fetchProjects().then(
      (page) => current && setProjects(page.items),
      () => {},
    )
    return () => {
      current = false
    }
  }, [])

  return (
    <section className="assistant" aria-labelledby="assistant-title">
      <div className="row">
        <h2 id="assistant-title">Assistent</h2>
        <div className="assistant-tools">
          <label>
            Bezug{' '}
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)} disabled={busy}>
              <option value="">Kein Projekt</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>
      <p className="muted assistant-note">
        Antwortet aus den Projekten, Aufgaben und dem Wissen, die du sehen darfst, und nennt die genutzten Artikel.
        {status.actions && ' Ändern kann der Assistent nur, was du darfst, und erst nachdem du die Änderung freigegeben hast.'} Das Gespräch wird nicht gespeichert.
        {status.model && <> Modell: {status.model}.</>}
      </p>
      <AssistantChat
        onOpenArticle={onOpenArticle}
        context={{ projectId: projectId || null }}
        examples={status.actions ? [...examples, actionExample] : examples}
        placeholder={status.actions ? 'Frag etwas oder sag, was der Assistent anlegen oder ändern soll …' : 'Frag nach Projekten, Aufgaben oder Wissen …'}
        onBusy={setBusy}
      />
    </section>
  )
}
