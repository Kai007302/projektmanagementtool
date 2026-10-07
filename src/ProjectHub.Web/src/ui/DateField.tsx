import { useRef, useState, type InputHTMLAttributes } from 'react'
import { formatDate, parseDate } from './dates'
import { CalendarIcon } from './icons'

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> & {
  /** The calendar day as YYYY-MM-DD, or '' for none. */
  value: string
  onChange: (value: string) => void
}

/**
 * A date field that takes what people type ("30.11.", "morgen", "Fr", "+3") and shows it as "30.11.2026"; the
 * calendar button opens the browser's date picker. Text that is no date marks the field invalid.
 */
export function DateField({ value, onChange, onBlur, className, ...input }: Props) {
  const [text, setText] = useState(() => formatDate(value))
  const [shown, setShown] = useState(value)
  const field = useRef<HTMLInputElement>(null)
  const picker = useRef<HTMLInputElement>(null)

  // A new value from outside (another field, a reload) replaces the text.
  if (value !== shown) {
    setShown(value)
    if (parseDate(text) !== value) setText(formatDate(value))
  }

  function change(next: string) {
    setText(next)
    const parsed = parseDate(next)
    field.current?.setCustomValidity(parsed === null ? 'Datum nicht erkannt. Zum Beispiel 30.11.2026, morgen, Fr oder +3.' : '')
    if (parsed !== null) {
      setShown(parsed)
      onChange(parsed)
    }
  }

  return (
    <span className={['date-field', className].filter(Boolean).join(' ')}>
      <input
        {...input}
        ref={field}
        type="text"
        inputMode="text"
        autoComplete="off"
        placeholder={input.placeholder ?? 'TT.MM.JJJJ'}
        title="Zum Beispiel 30.11.2026, 30.11., morgen, Fr oder +3"
        value={text}
        onChange={(event) => change(event.target.value)}
        onBlur={(event) => {
          const parsed = parseDate(text)
          if (parsed !== null) setText(formatDate(parsed))
          onBlur?.(event)
        }}
      />
      <button
        type="button"
        className="date-field-picker"
        // Typing is the main way; the picker is a mouse shortcut and stays out of the label and the tab order.
        aria-hidden="true"
        tabIndex={-1}
        title="Kalender öffnen"
        disabled={input.disabled}
        onClick={() => picker.current?.showPicker?.()}
      >
        <CalendarIcon />
      </button>
      <input
        ref={picker}
        className="date-field-native"
        type="date"
        tabIndex={-1}
        aria-hidden="true"
        value={shown}
        onChange={(event) => {
          change(formatDate(event.target.value))
          field.current?.focus()
        }}
      />
    </span>
  )
}
