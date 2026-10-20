import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ArticleDetails } from './api'
import { DataTableView } from './DataTableView'
import { SlideShow } from './SlideShow'
import type { InsightKind } from './useCases'

const kind: InsightKind = {
  articleType: 'experiment',
  label: 'Experiment',
  pluralLabel: 'Experimente',
  fields: [
    { key: 'question', label: 'Fragestellung', highlight: false },
    { key: 'insight', label: 'Erkenntnis', highlight: true },
  ],
  outcomes: [{ key: 'confirmed', label: 'bestätigt', tone: 'positive' }],
}

const details: ArticleDetails = {
  article: {
    id: 'a1',
    title: 'Wärme und Ausbeute',
    slug: 'waerme',
    articleType: 'experiment',
    summary: null,
    status: 'published',
    visibility: 'department',
    spaceId: null,
    spaceName: 'Labor',
    ownerId: null,
    ownerName: 'Kai',
    tags: [],
    updatedAt: '2026-10-20T10:00:00Z',
    publishedAt: null,
    version: 1,
    outcome: 'confirmed',
    confidence: 3,
  },
  content: {
    blocks: [
      {
        id: 'c',
        type: 'insight',
        fields: [
          { key: 'question', label: 'Fragestellung', text: 'Hilft Wärme?' },
          { key: 'insight', label: 'Erkenntnis', text: 'Ja, ab 40 °C.' },
        ],
      },
      { id: 't', type: 'data_table', title: 'Messreihe', columns: ['Temperatur', 'Ausbeute'], rows: [['20', '10'], ['40', '18']], chart: 'line' },
    ],
  },
  versionNumber: 1,
  reviewDueAt: null,
  capabilities: { canEdit: false, canAdmin: false },
  relations: [],
  references: [],
}

describe('slides', () => {
  it('walks through the article with the keyboard and closes with Esc', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(<SlideShow details={details} kind={kind} onClose={onClose} />)

    const dialog = screen.getByRole('dialog', { name: 'Folien: Wärme und Ausbeute' })
    expect(dialog).toHaveFocus()
    expect(screen.getByRole('heading', { name: 'Wärme und Ausbeute' })).toBeInTheDocument()
    expect(screen.getByText('bestätigt')).toBeInTheDocument()

    await user.keyboard('{ArrowRight}')
    expect(screen.getByText('Hilft Wärme?')).toBeInTheDocument()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('img', { name: /Liniendiagramm über Temperatur: Ausbeute von 10 bis 18/ })).toBeInTheDocument()
    await user.keyboard('{End}')
    expect(screen.getByText('Ja, ab 40 °C.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Weiter →' })).toBeDisabled()
    await user.keyboard('{Home}')
    expect(screen.getByText('1 / 4')).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })
})

describe('data tables', () => {
  it('switch between chart and table', async () => {
    const user = userEvent.setup()
    render(<DataTableView title="Messreihe" columns={['Tag', 'Wert']} rows={[['1', '2,5'], ['2', '3']]} chart="bar" />)
    expect(screen.getByRole('img', { name: /Balkendiagramm über Tag/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Tabelle' }))
    expect(screen.getByRole('columnheader', { name: 'Wert' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '2,5' })).toBeInTheDocument()
  })

  it('are only a table without numbers', () => {
    render(<DataTableView columns={['Probe', 'Notiz']} rows={[['A', 'gut']]} chart="line" />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Diagramm' })).toBeNull()
    expect(screen.getByRole('cell', { name: 'gut' })).toBeInTheDocument()
  })
})
