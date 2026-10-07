import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { CloseIcon } from './icons'

/**
 * A small window over the page for a rare task opened from a menu (subscribe a calendar, change the project symbol).
 * Escape, the close button or a click beside it closes it; focus goes back to where it was. It is rendered at the end
 * of the page, so a parent with its own stacking (the blurred header) cannot squeeze it into its area.
 */
export function Dialog({ label, title, onClose, children }: { label: string; title?: string; onClose: () => void; children: ReactNode }) {
  const box = useRef<HTMLDivElement>(null)
  const headingId = useId()

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    if (!box.current?.contains(document.activeElement)) box.current?.focus()
    return () => before?.focus?.()
  }, [])

  return createPortal(
    <div
      className="dialog-backdrop"
      onPointerDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        ref={box}
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-label={title ? undefined : label}
        aria-labelledby={title ? headingId : undefined}
        tabIndex={-1}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.stopPropagation()
            onClose()
          }
        }}
      >
        <button type="button" className="icon-button dialog-close" aria-label="Schließen" title="Schließen (Esc)" onClick={onClose}>
          <CloseIcon />
        </button>
        {title && (
          <h2 id={headingId} className="dialog-title">
            {title}
          </h2>
        )}
        {children}
      </div>
    </div>,
    document.body,
  )
}
