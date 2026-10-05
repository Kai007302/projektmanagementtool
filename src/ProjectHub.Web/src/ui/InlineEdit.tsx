import { useState, type ReactNode } from 'react'

type Props = {
  value: string
  /** Accessible name of the field and the button, e.g. "Projektname". */
  label: string
  onSave: (value: string) => Promise<unknown> | void
  editable: boolean
  maxLength?: number
  className?: string
  children?: ReactNode
}

/**
 * Text that turns into a field on click, like a title in Notion: Enter or leaving the field saves, Escape cancels.
 * Without the right to edit it is plain text.
 */
export function InlineEdit({ value, label, onSave, editable, maxLength = 200, className, children }: Props) {
  const [draft, setDraft] = useState<string | null>(null)

  if (!editable) return <span className={className}>{children ?? value}</span>

  if (draft === null) {
    return (
      <button type="button" className={['inline-edit', className].filter(Boolean).join(' ')} aria-label={`${label}: ${value}, bearbeiten`} title="Zum Bearbeiten klicken" onClick={() => setDraft(value)}>
        {children ?? value}
      </button>
    )
  }

  async function save() {
    const next = draft?.trim() ?? ''
    setDraft(null)
    if (next && next !== value) await onSave(next)
  }

  return (
    <input
      className={['inline-edit-input', className].filter(Boolean).join(' ')}
      aria-label={label}
      value={draft}
      maxLength={maxLength}
      autoFocus
      onFocus={(event) => event.currentTarget.select()}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={() => void save()}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault()
          event.currentTarget.blur()
        } else if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          setDraft(null)
        }
      }}
    />
  )
}
