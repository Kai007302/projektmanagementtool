import { useEffect, useRef, type ReactNode } from 'react'

/**
 * A small window over the page for a rare task opened from a menu (subscribe a calendar, change the project symbol).
 * Escape, the close button or a click beside it closes it; focus goes back to where it was.
 */
export function Dialog({ label, onClose, children }: { label: string; onClose: () => void; children: ReactNode }) {
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    box.current?.focus()
    return () => before?.focus?.()
  }, [])

  return (
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
        aria-label={label}
        tabIndex={-1}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.stopPropagation()
            onClose()
          }
        }}
      >
        <button type="button" className="icon-button dialog-close" aria-label="Schließen" title="Schließen (Esc)" onClick={onClose}>
          ✕
        </button>
        {children}
      </div>
    </div>
  )
}
