import { useState, type ReactNode } from 'react'
import { Dialog } from './Dialog'

/**
 * A quiet "+ …" button that opens a rarely used form in a small window over the page, instead of keeping the form on
 * the page or pushing the content aside. The form gets a function to close the window again.
 */
export function Reveal({
  label,
  title,
  plus = true,
  primary = false,
  className,
  children,
}: {
  label: string
  /** Heading of the window; by default "<label> hinzufügen" for "+" buttons. */
  title?: string
  plus?: boolean
  /** The page's main action, e.g. "+ Projekt": filled in the accent colour. */
  primary?: boolean
  className?: string
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  const heading = title ?? (plus && !label.includes(' ') ? `${label} hinzufügen` : label)
  return (
    <>
      <button type="button" className={[primary ? 'primary-button' : 'add-button', className].filter(Boolean).join(' ')} aria-haspopup="dialog" onClick={() => setOpen(true)}>
        {plus ? `+ ${label}` : label}
      </button>
      {open && (
        <Dialog label={heading} title={heading} onClose={() => setOpen(false)}>
          <div className="reveal">{children(() => setOpen(false))}</div>
        </Dialog>
      )}
    </>
  )
}
