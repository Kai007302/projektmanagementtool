import { outcomeOf, toneStyles, type InsightKind } from './useCases'
import './insights.css'

const confidenceLabels = ['', 'gering', 'mittel', 'hoch', 'sehr hoch']

/** The result of an insight as a colored chip, e.g. "Bestätigt"; the color repeats what the word says. */
export function OutcomeBadge({ kind, outcome }: { kind: InsightKind | null; outcome: string | null | undefined }) {
  const value = outcomeOf(kind, outcome)
  if (!value) return null
  return (
    <span className="outcome-badge" style={toneStyles[value.tone]}>
      {value.label}
    </span>
  )
}

type PickerProps = { kind: InsightKind; outcome: string | null | undefined; disabled?: boolean; onChange: (outcome: string) => void }

/** Changes the result; styled like the badge so editing looks like reading. */
export function OutcomePicker({ kind, outcome, disabled, onChange }: PickerProps) {
  const value = outcomeOf(kind, outcome) ?? kind.outcomes[0]
  return (
    <select
      className="outcome-badge outcome-picker"
      style={toneStyles[value.tone]}
      aria-label="Ergebnis"
      value={value.key}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    >
      {kind.outcomes.map((o) => (
        <option key={o.key} value={o.key}>
          {o.label}
        </option>
      ))}
    </select>
  )
}

type ConfidenceProps = { value: number | null | undefined; onChange?: (value: number | null) => void; disabled?: boolean }

/** How sure the result is, as four dots. With onChange the dots are a radio group; the chosen dot again clears it. */
export function Confidence({ value, onChange, disabled }: ConfidenceProps) {
  const level = value ?? 0
  if (!onChange) {
    if (!level) return null
    return (
      <span className="confidence" aria-label={`Sicherheit: ${confidenceLabels[level]}`}>
        Sicherheit
        <span className="confidence-dots" aria-hidden="true">
          {[1, 2, 3, 4].map((n) => (
            <span key={n} className={n <= level ? 'dot on' : 'dot'} />
          ))}
        </span>
        {confidenceLabels[level]}
      </span>
    )
  }
  return (
    <span className="confidence" role="radiogroup" aria-label="Sicherheit">
      Sicherheit
      <span className="confidence-dots">
        {[1, 2, 3, 4].map((n) => (
          <button
            key={n}
            type="button"
            role="radio"
            aria-checked={n === level}
            aria-label={confidenceLabels[n]}
            className={n <= level ? 'dot on' : 'dot'}
            disabled={disabled}
            onClick={() => onChange(n === level ? null : n)}
          />
        ))}
      </span>
      {level ? confidenceLabels[level] : 'offen'}
    </span>
  )
}
