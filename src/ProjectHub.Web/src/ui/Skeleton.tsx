/**
 * Grey placeholders in the shape of what is loading (tiles, a board, a list, lines of text), so the page shows its
 * layout at once instead of "… wird geladen". Screen readers hear the label.
 */
export function Skeleton({ label, kind = 'lines', count = 3 }: { label: string; kind?: 'tiles' | 'board' | 'list' | 'lines'; count?: number }) {
  const items = Array.from({ length: count }, (_, i) => i)
  return (
    <div className={`skeleton skeleton-${kind}`} role="status">
      <span className="visually-hidden">{label} …</span>
      {kind === 'board'
        ? items.map((column) => (
            <div key={column} className="skeleton-column" aria-hidden="true">
              <span className="skeleton-bar short" />
              {Array.from({ length: 3 - (column % 2) }, (_, card) => (
                <span key={card} className="skeleton-block card" />
              ))}
            </div>
          ))
        : items.map((item) => <span key={item} className={kind === 'lines' ? 'skeleton-bar' : 'skeleton-block'} aria-hidden="true" />)}
    </div>
  )
}
