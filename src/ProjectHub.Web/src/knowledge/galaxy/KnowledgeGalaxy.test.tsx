import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../../test/fakeApi'
import { KnowledgeGalaxy } from './KnowledgeGalaxy'

const graph = {
  nodes: [
    { id: 'a-1', title: 'Deployment-Prozess', articleType: 'process', status: 'published', spaceId: 's-1', summary: 'Vom PR in die Produktion.', degree: 2 },
    { id: 'a-2', title: 'Störungen melden', articleType: 'faq', status: 'published', spaceId: 's-1', summary: null, degree: 1 },
    { id: 'a-3', title: 'Release-Checkliste', articleType: 'checklist', status: 'published', spaceId: null, summary: null, degree: 1 },
  ],
  edges: [
    { id: 'e-1', source: 'a-2', target: 'a-1', relationType: 'RELATED' },
    { id: 'e-2', source: 'a-3', target: 'a-1', relationType: 'REQUIRES' },
  ],
  truncated: false,
}

const spaces = [{ id: 's-1', name: 'IT & Plattform', description: null, articleCount: 2, version: 1 }]

describe('KnowledgeGalaxy', () => {
  beforeEach(() => {
    // jsdom has no canvas; the view must cope without drawing.
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('shows the galaxy with a keyboard-usable canvas, legend and summary', async () => {
    fakeApi({ 'GET /api/v1/knowledge/graph': () => json(graph) })
    render(<KnowledgeGalaxy spaces={spaces} onOpenArticle={() => {}} />)

    const canvas = await screen.findByRole('img', { name: 'Wissensgalaxie mit 3 Artikeln und 2 Beziehungen' })
    expect(canvas).toHaveAttribute('tabindex', '0')
    expect(screen.getByText(/3 Artikel, 2 Beziehungen/)).toBeInTheDocument()
    const legend = within(screen.getByRole('region', { name: 'Legende' }))
    expect(legend.getByText('Prozess')).toBeInTheDocument()
    expect(legend.queryByText('Glossar')).not.toBeInTheDocument()
  })

  it('navigates along relations without a mouse and has no separate list view', async () => {
    const open = vi.fn()
    fakeApi({ 'GET /api/v1/knowledge/graph': () => json(graph) })
    render(<KnowledgeGalaxy spaces={spaces} onOpenArticle={open} />)

    await userEvent.selectOptions(await screen.findByLabelText('Artikel fokussieren'), 'a-2')
    expect(screen.queryByRole('button', { name: 'Liste' })).not.toBeInTheDocument()

    const details = within(screen.getByRole('region', { name: 'Störungen melden' }))
    expect(details.getByText('Verwandt mit')).toBeInTheDocument()
    await userEvent.click(details.getByRole('button', { name: 'Deployment-Prozess' }))

    const next = within(screen.getByRole('region', { name: 'Deployment-Prozess' }))
    expect(next.getByText('Vom PR in die Produktion.')).toBeInTheDocument()
    expect(next.getByText('Ist Voraussetzung für')).toBeInTheDocument()
    await userEvent.click(next.getByRole('button', { name: 'Artikel öffnen' }))
    expect(open).toHaveBeenCalledWith('a-1')
  })

  it('focuses an article chosen from the select and filters by space and type', async () => {
    const api = fakeApi({
      'GET /api/v1/knowledge/graph': () => json(graph),
      'GET /api/v1/knowledge/graph?spaceId=s-1&type=faq': () => json({ ...graph, nodes: [graph.nodes[1]], edges: [] }),
    })
    render(<KnowledgeGalaxy spaces={spaces} onOpenArticle={() => {}} />)

    await userEvent.selectOptions(await screen.findByLabelText('Artikel fokussieren'), 'a-3')
    expect(screen.getByRole('region', { name: 'Release-Checkliste' })).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Kategorie'), 's-1')
    await userEvent.selectOptions(screen.getByLabelText('Art'), 'faq')

    expect(await screen.findByRole('img', { name: 'Wissensgalaxie mit 1 Artikel und 0 Beziehungen' })).toBeInTheDocument()
    expect(api.calls.map((c) => c.key)).toContain('GET /api/v1/knowledge/graph?spaceId=s-1&type=faq')
    expect(screen.getByRole('region', { name: 'Auswahl' })).toBeInTheDocument()
  })

  it('opens the galaxy in full screen and leaves it with Escape', async () => {
    fakeApi({ 'GET /api/v1/knowledge/graph': () => json(graph) })
    render(<KnowledgeGalaxy spaces={spaces} onOpenArticle={() => {}} />)

    const canvas = await screen.findByRole('img', { name: /Wissensgalaxie/ })
    await userEvent.click(screen.getByRole('button', { name: 'Vollbild' }))
    expect(canvas.parentElement).toHaveClass('fullscreen')
    expect(screen.getByRole('button', { name: 'Vollbild beenden' })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.keyboard('{Escape}')
    expect(canvas.parentElement).not.toHaveClass('fullscreen')
  })
})
