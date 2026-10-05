import { useState, type CSSProperties } from 'react'
import { colors, type Color } from './model'
import { fills, strokes } from './palette'

/** Data type for dragging a note from the stack onto the canvas; the value is the color. */
export const stickyDragType = 'application/x-projecthub-sticky'

type Props = {
  color: Color
  onColor: (color: Color) => void
  onAdd: () => void
  onBulk: (lines: string[]) => void
}

/**
 * The sticky note stack, modelled on Miro: click for a note in the middle of the view or drag one onto the board,
 * pick one of sixteen colors, or paste several lines to get one note per line.
 */
export function StickyStack({ color, onColor, onAdd, onBulk }: Props) {
  const [open, setOpen] = useState(false)
  const [bulk, setBulk] = useState('')
  const sheet = { '--sheet-fill': fills[color], '--sheet-stroke': strokes[color] } as CSSProperties

  return (
    <span className="sticky-stack">
      <button
        type="button"
        className="board-tool sticky-stack-pile"
        draggable
        aria-label="Notiz hinzufügen"
        title="Klicken oder auf das Whiteboard ziehen (Taste N)"
        onDragStart={(event) => {
          event.dataTransfer.setData(stickyDragType, color)
          event.dataTransfer.effectAllowed = 'copy'
        }}
        onClick={onAdd}
      >
        <span className="sticky-stack-sheets" style={sheet} aria-hidden="true">
          <span />
          <span />
          <span />
        </span>
      </button>
      <button type="button" className="sticky-stack-toggle" title="Notizfarbe" aria-expanded={open} aria-label={`Notizfarbe und mehrere Notizen (${colors[color]})`} onClick={() => setOpen(!open)}>
        <span className="sticky-swatch" style={{ background: fills[color], borderColor: strokes[color] }} aria-hidden="true" />
      </button>
      {open && (
        <div
          className="board-popover sticky-stack-popover"
          role="group"
          aria-label="Notizen"
          onKeyDown={(event) => {
            if (event.key === 'Escape') setOpen(false)
          }}
        >
          <div className="sticky-swatches" role="group" aria-label="Notizfarbe">
            {(Object.entries(colors) as [Color, string][]).map(([value, label]) => (
              <button
                key={value}
                type="button"
                className="sticky-swatch"
                aria-label={label}
                aria-pressed={value === color}
                title={label}
                style={{ background: fills[value], borderColor: strokes[value] }}
                onClick={() => {
                  onColor(value)
                  setOpen(false)
                }}
              />
            ))}
          </div>
          <label className="stacked">
            Mehrere Notizen, eine pro Zeile
            <textarea rows={4} maxLength={5000} value={bulk} onChange={(event) => setBulk(event.target.value)} />
          </label>
          <button
            type="button"
            disabled={!bulk.trim()}
            onClick={() => {
              onBulk(bulk.split('\n'))
              setBulk('')
              setOpen(false)
            }}
          >
            Notizen einfügen
          </button>
        </div>
      )}
    </span>
  )
}
