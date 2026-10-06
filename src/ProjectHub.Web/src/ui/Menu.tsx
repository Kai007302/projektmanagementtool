import { useEffect, useRef, useState, type ReactNode } from 'react'

export type MenuItem = { label: string; onSelect: () => void; danger?: boolean; disabled?: boolean }

/**
 * The "…" button for rare actions (rename, move, delete), like in Notion or Linear. The menu closes after a choice,
 * on Escape and on a click elsewhere.
 */
type Props = {
  label: string
  items: MenuItem[]
  className?: string
  /** What the button shows instead of the three dots, e.g. the person's avatar. */
  trigger?: ReactNode
}

export function Menu({ label, items, className, trigger }: Props) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    root.current?.querySelector<HTMLButtonElement>('[role="menuitem"]:not(:disabled)')?.focus()
    const onDown = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
  }, [open])

  if (items.length === 0) return null

  return (
    <div
      ref={root}
      className={['menu', className].filter(Boolean).join(' ')}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) {
          event.stopPropagation()
          setOpen(false)
          button.current?.focus()
        }
      }}
    >
      <button
        ref={button}
        type="button"
        className={trigger ? 'menu-button menu-trigger' : 'menu-button'}
        aria-label={label}
        title={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        {trigger ?? (
          <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">
            <circle cx="3" cy="8" r="1.5" />
            <circle cx="8" cy="8" r="1.5" />
            <circle cx="13" cy="8" r="1.5" />
          </svg>
        )}
      </button>
      {open && (
        <div className="menu-list" role="menu" aria-label={label}>
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              className={item.danger ? 'menu-item danger-item' : 'menu-item'}
              disabled={item.disabled}
              onClick={() => {
                setOpen(false)
                item.onSelect()
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
