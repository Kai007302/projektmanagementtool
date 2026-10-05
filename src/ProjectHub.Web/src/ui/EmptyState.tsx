import type { ReactNode } from 'react'

/** Friendly empty state: a big emoji, one line of text and an optional hint or action. */
export function EmptyState({ emoji, children, hint }: { emoji: string; children: ReactNode; hint?: ReactNode }) {
  return (
    <div className="empty-state">
      <span className="empty-state-emoji" aria-hidden="true">
        {emoji}
      </span>
      <p>{children}</p>
      {hint && <p className="muted">{hint}</p>}
    </div>
  )
}
