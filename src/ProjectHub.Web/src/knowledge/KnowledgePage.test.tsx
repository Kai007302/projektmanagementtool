import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Me } from '../identity/api'
import { fakeApi, json } from '../test/fakeApi'
import { KnowledgePage } from './KnowledgePage'

const ben: Me = { id: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', organizationId: 'org-1', organizationRole: 'member' }
const empty = { items: [], nextOffset: null }

const summary = {
  id: 'a-1',
  title: 'Deployment-Prozess',
  slug: 'deployment-prozess',
  articleType: 'process',
  summary: 'Wie Änderungen in Produktion kommen.',
  status: 'published',
  visibility: 'organization',
  spaceId: 's-1',
  spaceName: 'IT & Plattform',
  ownerId: 'u-ada',
  ownerName: 'Ada Admin',
  tags: ['Betrieb'],
  updatedAt: '2026-10-01T10:00:00Z',
  publishedAt: '2026-10-01T10:00:00Z',
  version: 4,
}

const blocks = [
  { id: 'b1', type: 'heading', level: 2, text: 'Ablauf' },
  { id: 'b2', type: 'numbered_list', items: ['Review', 'Freigabe'] },
  { id: 'b3', type: 'callout', tone: 'warning', text: 'Nur nach Freigabe.' },
  { id: 'b4', type: 'project_reference', projectId: 'p-1' },
  { id: 'b5', type: 'task_reference', taskId: 't-hidden' },
  { id: 'b6', type: 'link', url: 'javascript:alert(1)', label: 'Böse' },
]

function details(capabilities = { canEdit: false, canAdmin: false }, overrides: Record<string, unknown> = {}) {
  return {
    article: { ...summary, ...overrides },
    content: { blocks },
    versionNumber: 2,
    reviewDueAt: null,
    capabilities,
    relations: [{ id: 'r-1', relationType: 'RELATED', direction: 'incoming', articleId: 'a-2', title: 'Störungen melden', articleType: 'faq' }],
    references: [],
  }
}

function routes(article = details()) {
  return {
    'GET /api/v1/knowledge/articles?limit=100': () => json({ items: [summary], nextOffset: null }),
    'GET /api/v1/knowledge/spaces': () => json([{ id: 's-1', name: 'IT & Plattform', description: null, articleCount: 1, version: 1 }]),
    'GET /api/v1/knowledge/tags': () => json([{ name: 'Betrieb', articleCount: 2 }]),
    'GET /api/v1/knowledge/articles/a-1': () => json(article),
    'GET /api/v1/knowledge/articles/a-1/comments?limit=100': () => json(empty),
    'GET /api/v1/knowledge/articles/a-1/versions': () =>
      json([
        { id: 'v-2', versionNumber: 2, createdBy: 'u-ada', createdByName: 'Ada Admin', changeNote: 'Freigabe ergänzt', createdAt: '2026-10-01T10:00:00Z' },
        { id: 'v-1', versionNumber: 1, createdBy: 'u-ada', createdByName: 'Ada Admin', changeNote: 'Erstellt', createdAt: '2026-09-01T10:00:00Z' },
      ]),
    'GET /api/v1/knowledge/articles/a-1/permissions': () => json([]),
    'GET /api/v1/projects/p-1': () => json({ id: 'p-1', name: 'Intranet-Relaunch' }),
    'GET /api/v1/users?limit=100': () => json(empty),
    'GET /api/v1/teams?limit=100': () => json(empty),
    'GET /api/v1/projects?limit=100': () => json(empty),
  }
}

async function openArticle() {
  await userEvent.click(await screen.findByRole('button', { name: /Deployment-Prozess/ }))
  return await screen.findByRole('heading', { name: 'Deployment-Prozess', level: 2 })
}

describe('KnowledgePage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists articles and searches while typing, with filters as chips', async () => {
    const api = fakeApi({ ...routes(), 'GET /api/v1/knowledge/articles?limit=100&q=Freigabe&type=process&tag=Betrieb': () => json(empty) })
    render(<KnowledgePage me={ben} />)

    expect(await screen.findByRole('button', { name: /Deployment-Prozess.*Prozess · Veröffentlicht · IT & Plattform/ })).toBeInTheDocument()

    const search = screen.getByRole('search')
    await userEvent.type(within(search).getByLabelText('Suche'), 'Freigabe')
    await userEvent.selectOptions(within(search).getByLabelText('Art'), 'process')
    await userEvent.selectOptions(within(search).getByLabelText('Tag'), 'Betrieb')
    expect(within(search).queryByRole('button', { name: 'Suchen' })).not.toBeInTheDocument()

    expect(await screen.findByText('Keine Artikel gefunden.')).toBeInTheDocument()
    expect(api.calls.map((c) => c.key)).toContain('GET /api/v1/knowledge/articles?limit=100&q=Freigabe&type=process&tag=Betrieb')
  })

  it('renders blocks as text and resolves references the reader can see', async () => {
    fakeApi(routes())
    render(<KnowledgePage me={ben} />)
    await openArticle()

    expect(screen.getByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
    expect(screen.getByRole('complementary', { name: 'Warnung' })).toHaveTextContent('Nur nach Freigabe.')
    expect(await screen.findByText('Intranet-Relaunch')).toBeInTheDocument()
    expect(await screen.findByText('nicht verfügbar')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Böse' })).not.toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Beziehungen' })).getByText('Verwandt mit')).toBeInTheDocument()
  })

  it('offers readers no changes', async () => {
    fakeApi(routes())
    render(<KnowledgePage me={ben} />)
    await openArticle()

    expect(screen.queryByRole('button', { name: 'Bearbeiten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Archivieren|Veröffentlichen/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Weitere Aktionen zum Artikel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Freigaben' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /wiederherstellen/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Kommentieren' })).toBeInTheDocument()
  })

  it('edits blocks and saves them as a new version', async () => {
    const api = fakeApi({
      ...routes(details({ canEdit: true, canAdmin: false })),
      'PUT /api/v1/knowledge/articles/a-1/content': () => json(details({ canEdit: true, canAdmin: false })),
    })
    render(<KnowledgePage me={ben} />)
    await openArticle()

    await userEvent.click(screen.getByRole('button', { name: 'Bearbeiten' }))
    const editor = screen.getByRole('form', { name: 'Artikel bearbeiten' })
    await userEvent.click(within(editor).getByRole('button', { name: 'Block 2 nach oben' }))
    await userEvent.click(within(editor).getByRole('button', { name: 'Block 6 entfernen' }))
    await userEvent.selectOptions(within(editor).getByLabelText('Neuer Block'), 'paragraph')
    await userEvent.click(within(editor).getByRole('button', { name: 'Block hinzufügen' }))
    await userEvent.type(within(editor).getByLabelText('Absatz 6'), 'Danach informieren.')
    await userEvent.type(within(editor).getByLabelText('Änderungsnotiz'), 'Info ergänzt')
    await userEvent.click(within(editor).getByRole('button', { name: 'Speichern' }))

    const body = JSON.parse(String(api.calls.find((c) => c.key === 'PUT /api/v1/knowledge/articles/a-1/content')?.init?.body))
    expect(body.version).toBe(4)
    expect(body.changeNote).toBe('Info ergänzt')
    expect(body.content.blocks.map((b: { type: string }) => b.type)).toEqual([
      'numbered_list',
      'heading',
      'callout',
      'project_reference',
      'task_reference',
      'paragraph',
    ])
    expect(body.content.blocks[5].text).toBe('Danach informieren.')
    expect(api.calls.some((c) => c.key.startsWith('PATCH'))).toBe(false)
  })

  it('reports concurrent changes', async () => {
    fakeApi({
      ...routes(details({ canEdit: true, canAdmin: false })),
      'PUT /api/v1/knowledge/articles/a-1/content': () => json({ title: 'Conflict' }, 409),
    })
    render(<KnowledgePage me={ben} />)
    await openArticle()

    await userEvent.click(screen.getByRole('button', { name: 'Bearbeiten' }))
    await userEvent.click(screen.getByRole('button', { name: 'Block 1 entfernen' }))
    await userEvent.click(screen.getByRole('button', { name: 'Speichern' }))

    expect(await screen.findByText(/Jemand anderes hat den Artikel inzwischen geändert/)).toBeInTheDocument()
  })

  it('lets article admins archive, restore versions and manage permissions', async () => {
    const api = fakeApi({
      ...routes(details({ canEdit: true, canAdmin: true })),
      'POST /api/v1/knowledge/articles/a-1/status': () => json(details({ canEdit: true, canAdmin: true }, { status: 'archived' })),
      'POST /api/v1/knowledge/articles/a-1/versions/1/restore': () => json(details({ canEdit: true, canAdmin: true })),
    })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<KnowledgePage me={ben} />)
    await openArticle()

    await userEvent.click(screen.getByRole('button', { name: 'Weitere Aktionen zum Artikel' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Archivieren' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Version 1 wiederherstellen' }))

    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/knowledge/articles/a-1/status')?.init?.body))).toEqual({ version: 4, status: 'archived' })
    expect(api.calls.map((c) => c.key)).toContain('POST /api/v1/knowledge/articles/a-1/versions/1/restore')
    expect(screen.getByRole('region', { name: 'Freigaben' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Zur Prüfung geben' })).not.toBeInTheDocument()
  })

  it('creates a draft with one click and opens it in the editor', async () => {
    const created = details({ canEdit: true, canAdmin: true }, { id: 'a-1', title: 'Neuer Artikel', status: 'draft' })
    const api = fakeApi({ ...routes(created), 'POST /api/v1/knowledge/articles': () => json(created, 201) })
    render(<KnowledgePage me={ben} />)

    await userEvent.click(await screen.findByRole('button', { name: '+ Artikel' }))

    expect(await screen.findByRole('form', { name: 'Artikel bearbeiten' })).toBeInTheDocument()
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/knowledge/articles')?.init?.body))).toEqual({
      title: 'Neuer Artikel',
      articleType: 'article',
      summary: '',
      spaceId: null,
      visibility: 'organization',
    })
  })
})
