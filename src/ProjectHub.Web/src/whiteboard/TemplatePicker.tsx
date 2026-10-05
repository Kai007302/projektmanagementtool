import { useEffect, useRef } from 'react'
import { boundsOf, type NewObject } from './model'
import { diamondPoints, fills, strokes } from './palette'
import { templates, type Template } from './templates'

type Props = {
  onPick: (template: Template) => void
  onClose: () => void
}

/** The list of templates; picking one places it in the middle of the visible board. */
export function TemplatePicker({ onPick, onClose }: Props) {
  const first = useRef<HTMLButtonElement>(null)
  useEffect(() => first.current?.focus(), [])

  return (
    <section
      className="board-templates"
      aria-labelledby="board-templates-heading"
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <header className="board-templates-header">
        <h4 id="board-templates-heading">Vorlage einfügen</h4>
        <button type="button" className="link-button" onClick={onClose}>
          Schließen
        </button>
      </header>
      <ul className="board-template-list">
        {templates.map((template, index) => (
          <li key={template.id}>
            <button ref={index === 0 ? first : undefined} type="button" className="board-template" onClick={() => onPick(template)}>
              <TemplatePreview items={template.items} />
              <strong>{template.name}</strong>
              <span className="muted">{template.description}</span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

/** A small drawing of the template's shapes, without text. */
function TemplatePreview({ items }: { items: NewObject[] }) {
  const box = boundsOf(items)
  const pad = 20
  return (
    <svg className="board-template-preview" viewBox={`${box.x - pad} ${box.y - pad} ${box.w + 2 * pad} ${box.h + 2 * pad}`} aria-hidden="true">
      {items.map((item, index) => {
        const color = item.color ?? 'white'
        const shape = { fill: fills[color], stroke: strokes[color], strokeWidth: 3 }
        const w = item.w ?? 0
        const h = item.h ?? 0
        switch (item.type) {
          case 'arrow':
            return <line key={index} x1={item.x} y1={item.y} x2={item.x2} y2={item.y2} stroke={strokes.gray} strokeWidth={5} />
          case 'ellipse':
            return <ellipse key={index} cx={item.x + w / 2} cy={item.y + h / 2} rx={w / 2} ry={h / 2} {...shape} />
          case 'diamond':
            return <polygon key={index} points={diamondPoints(item.x, item.y, w, h)} {...shape} />
          case 'text':
            return <rect key={index} x={item.x} y={item.y + h / 4} width={Math.min(w, 260)} height={h / 2} rx={6} fill={strokes.gray} opacity={0.35} />
          default:
            return <rect key={index} x={item.x} y={item.y} width={w} height={h} rx={4} {...shape} />
        }
      })}
    </svg>
  )
}
