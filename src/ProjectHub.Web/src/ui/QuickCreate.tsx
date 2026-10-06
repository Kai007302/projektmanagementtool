import { useState, type FormEvent } from 'react'

type Props = {
  /** Text of the button, e.g. "Aufgabe" for "+ Aufgabe". */
  label: string
  /** Accessible name of the field, e.g. "Neue Aufgabe in Offen". */
  fieldLabel: string
  onCreate: (title: string) => Promise<void>
  className?: string
}

/**
 * Trello's "+ Karte hinzufügen": a quiet button that becomes a single field. Enter creates and keeps the field open
 * for the next one; Escape or leaving an empty field closes it.
 */
export function QuickCreate({ label, fieldLabel, onCreate, className }: Props) {
  const [open, setOpen] = useState(false)
  const [title, setTitle] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const value = title.trim()
    if (!value) return
    setError(null)
    try {
      await onCreate(value)
      setTitle('')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!open) {
    return (
      <button type="button" className={['add-button', className].filter(Boolean).join(' ')} onClick={() => setOpen(true)}>
        + {label}
      </button>
    )
  }

  return (
    <form className={['quick-create', className].filter(Boolean).join(' ')} onSubmit={submit}>
      <input
        aria-label={fieldLabel}
        placeholder="Titel eingeben, Enter zum Anlegen"
        value={title}
        maxLength={500}
        autoFocus
        onChange={(event) => setTitle(event.target.value)}
        onBlur={() => {
          if (!title.trim()) setOpen(false)
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.stopPropagation()
            setTitle('')
            setOpen(false)
          }
        }}
      />
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
