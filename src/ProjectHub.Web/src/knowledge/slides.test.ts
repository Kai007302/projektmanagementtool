import { describe, expect, it } from 'vitest'
import type { Block } from './blocks'
import { buildSlides } from './slides'
import type { InsightKind } from './useCases'

const experiment: InsightKind = {
  articleType: 'experiment',
  label: 'Experiment',
  pluralLabel: 'Experimente',
  fields: [
    { key: 'question', label: 'Fragestellung', highlight: false },
    { key: 'setup', label: 'Aufbau', highlight: false },
    { key: 'insight', label: 'Erkenntnis', highlight: true },
  ],
  outcomes: [{ key: 'running', label: 'läuft', tone: 'active' }],
}

describe('slides', () => {
  it('shows the card first, the text by sections, the data on its own and the insight last', () => {
    const blocks: Block[] = [
      {
        id: 'c',
        type: 'insight',
        fields: [
          { key: 'question', label: 'Fragestellung', text: 'Hilft Wärme?' },
          { key: 'setup', label: 'Aufbau', text: ' ' },
          { key: 'insight', label: 'Erkenntnis', text: 'Ja, ab 40 °C.' },
        ],
      },
      { id: 'p1', type: 'paragraph', text: 'Vorab' },
      { id: 'h1', type: 'heading', level: 2, text: 'Ablauf' },
      { id: 'h2', type: 'heading', level: 3, text: 'Details' },
      { id: 'p2', type: 'paragraph', text: 'Schritt' },
      { id: 't', type: 'data_table', columns: ['x', 'y'], rows: [['1', '2']], chart: 'line' },
      { id: 'e', type: 'data_table', columns: [], rows: [], chart: 'none' },
    ]
    const slides = buildSlides(blocks, experiment)
    expect(slides.map((s) => s.type)).toEqual(['title', 'field', 'section', 'section', 'data', 'field'])
    expect(slides[1]).toMatchObject({ field: { key: 'question' }, highlight: false })
    expect(slides[2]).toMatchObject({ heading: null, blocks: [{ id: 'p1' }] })
    expect(slides[3]).toMatchObject({ heading: 'Ablauf', blocks: [{ id: 'h2' }, { id: 'p2' }] })
    expect(slides[5]).toMatchObject({ field: { key: 'insight' }, highlight: true })
  })

  it('is just a title and the text for an ordinary article', () => {
    const slides = buildSlides([{ id: 'p', type: 'paragraph', text: 'Text' }], null)
    expect(slides.map((s) => s.type)).toEqual(['title', 'section'])
  })
})
