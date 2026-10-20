import type { Block, InsightBlockField } from './blocks'
import type { InsightKind } from './useCases'

export type Slide =
  | { type: 'title' }
  | { type: 'field'; field: InsightBlockField; highlight: boolean }
  | { type: 'section'; heading: string | null; blocks: Block[] }
  | { type: 'data'; block: Extract<Block, { type: 'data_table' }> }

/**
 * An article as slides (ADR 0023): a title slide, one slide per filled card field, one per section of the text (a heading of
 * level 1 or 2 starts a new one), one per data table, and the result last. Nothing is stored, the slides follow the article.
 */
export function buildSlides(blocks: Block[], kind: InsightKind | null): Slide[] {
  const slides: Slide[] = [{ type: 'title' }]
  const card = blocks.find((block): block is Extract<Block, { type: 'insight' }> => block.type === 'insight')
  const highlightKey = kind?.fields.find((f) => f.highlight)?.key ?? card?.fields[card.fields.length - 1]?.key
  const filled = card?.fields.filter((field) => field.text.trim()) ?? []
  const result = filled.find((field) => field.key === highlightKey)
  for (const field of filled) if (field !== result) slides.push({ type: 'field', field, highlight: false })

  let section: Extract<Slide, { type: 'section' }> | null = null
  const flush = () => {
    if (section && (section.blocks.length > 0 || section.heading)) slides.push(section)
    section = null
  }
  for (const block of blocks) {
    if (block.type === 'insight') continue
    if (block.type === 'data_table') {
      flush()
      if (block.columns.length > 0) slides.push({ type: 'data', block })
    } else if (block.type === 'heading' && block.level <= 2) {
      flush()
      section = { type: 'section', heading: block.text, blocks: [] }
    } else {
      section ??= { type: 'section', heading: null, blocks: [] }
      section.blocks.push(block)
    }
  }
  flush()

  if (result) slides.push({ type: 'field', field: result, highlight: true })
  return slides
}
