import type { AssistantAction } from './api'

export type ActionState = AssistantAction & { decision?: boolean; dropped?: boolean }

type Props = { action: ActionState; disabled: boolean; onDecide: (approved: boolean) => void }

/**
 * One change the assistant proposes (ADR 0016), shown in the person's words. Nothing happens in ProjectHub until the
 * person clicks "Ausführen"; "Ablehnen" tells the assistant no.
 */
export function ActionCard({ action, disabled, onDecide }: Props) {
  const state =
    action.decision === true ? 'Freigegeben' : action.decision === false ? 'Abgelehnt' : action.dropped ? 'Nicht ausgeführt' : null
  return (
    <section
      className={action.destructive ? 'assistant-action destructive' : 'assistant-action'}
      aria-label={`Vorschlag: ${action.title}`}
    >
      <h3>{action.title}</h3>
      {action.details.length > 0 && (
        <dl>
          {action.details.map((detail, index) => (
            <div key={index}>
              <dt>{detail.label}</dt>
              <dd>{detail.value}</dd>
            </div>
          ))}
        </dl>
      )}
      {action.text && (
        <div className="assistant-action-text" tabIndex={0} aria-label="Text">
          {action.text}
        </div>
      )}
      {state ? (
        <p className="muted assistant-action-state">{state}</p>
      ) : (
        <div className="assistant-action-buttons">
          <button type="button" className={action.destructive ? 'danger' : 'assistant-approve'} disabled={disabled} onClick={() => onDecide(true)}>
            {action.destructive ? 'Löschen' : 'Ausführen'}
          </button>
          <button type="button" disabled={disabled} onClick={() => onDecide(false)}>
            Ablehnen
          </button>
        </div>
      )}
    </section>
  )
}
