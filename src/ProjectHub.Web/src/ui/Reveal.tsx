import { useState, type ReactNode } from 'react'

/**
 * A quiet "+ …" button that shows a rarely used form only when someone wants it, instead of keeping the form open on
 * the page. The form gets a function to close itself again.
 */
export function Reveal({ label, plus = true, className, children }: { label: string; plus?: boolean; className?: string; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false)
  if (!open) {
    return (
      <button type="button" className={['add-button', className].filter(Boolean).join(' ')} onClick={() => setOpen(true)}>
        {plus ? `+ ${label}` : label}
      </button>
    )
  }
  return (
    <div
      className="reveal"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation()
          setOpen(false)
        }
      }}
    >
      {children(() => setOpen(false))}
    </div>
  )
}
