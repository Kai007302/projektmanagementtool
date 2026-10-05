import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as Y from 'yjs'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { fakeApi, json } from '../test/fakeApi'
import type { Whiteboard } from './api'
import { addObject, readObjects } from './model'
import type { SyncHandlers } from './sync'
import { WhiteboardPanel } from './WhiteboardPanel'

const sessions: { doc: Y.Doc; handlers: SyncHandlers; presence: unknown[]; stopped: boolean }[] = []

vi.mock('./sync', () => ({
  remoteOrigin: Symbol('remote'),
  connectWhiteboard: vi.fn((_id: string, doc: Y.Doc, handlers: SyncHandlers) => {
    const session = { doc, handlers, presence: [] as unknown[], stopped: false }
    sessions.push(session)
    return { sendPresence: (...args: unknown[]) => session.presence.push(args), stop: () => (session.stopped = true) }
  }),
}))

const ben: Me = { id: 'u-ben', displayName: 'Ben', email: 'ben@x', organizationId: 'org-1', organizationRole: 'member' }

const all = { canContribute: true, canEdit: true, canManage: true }
const viewerRights = { canContribute: false, canEdit: false, canManage: false }

function project(capabilities = all): ProjectDetails {
  return {
    id: 'p-1',
    name: 'Intranet',
    description: null,
    status: 'active',
    startDate: null,
    endDate: null,
    version: 1,
    ownerId: 'u-ben',
    capabilities,
    members: [],
  }
}

const board = (id: string, name: string): Whiteboard => ({ id, projectId: 'p-1', name, createdAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z', version: 1 })

const current = () => sessions[sessions.length - 1]

async function connect(canEdit: boolean) {
  await screen.findByRole('application')
  act(() => current().handlers.onStatus('online', canEdit))
}

describe('WhiteboardPanel', () => {
  beforeEach(() => {
    sessions.length = 0
    Element.prototype.setPointerCapture = () => {}
  })
  afterEach(() => vi.unstubAllGlobals())

  it('creates a whiteboard and opens it', async () => {
    let boards = [board('b-1', 'Ideen')]
    const api = fakeApi({
      'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: boards, nextOffset: null }),
      'POST /api/v1/projects/p-1/whiteboards': () => {
        boards = [...boards, board('b-2', 'Workshop')]
        return json(boards[1], 201)
      },
    })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)

    expect(await screen.findByRole('heading', { name: 'Ideen' })).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Neues Whiteboard'), 'Workshop')
    await userEvent.click(screen.getByRole('button', { name: 'Anlegen' }))

    expect(await screen.findByRole('heading', { name: 'Workshop' })).toBeInTheDocument()
    expect(api.calls.some((c) => c.key === 'POST /api/v1/projects/p-1/whiteboards' && String(c.init?.body).includes('Workshop'))).toBe(true)
    expect(sessions.at(-2)?.stopped).toBe(true)
  })

  it('adds, edits, moves, deletes and undoes objects through the toolbar, list and keyboard', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }) })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)
    await connect(true)

    await userEvent.click(screen.getByRole('button', { name: '+ Notiz' }))
    const text = screen.getByLabelText('Text')
    await userEvent.clear(text)
    await userEvent.type(text, 'Idee')
    expect(within(screen.getByRole('region', { name: /Objekte/ })).getByRole('button', { name: 'Notiz: Idee' })).toBeInTheDocument()
    expect(readObjects(current().doc)[0].text).toBe('Idee')

    const before = readObjects(current().doc)[0]
    screen.getByRole('application').focus()
    await userEvent.keyboard('{ArrowRight}{Shift>}{ArrowDown}{/Shift}')
    expect(readObjects(current().doc)[0]).toMatchObject({ x: before.x + 10, y: before.y + 1 })

    await userEvent.keyboard('{Delete}')
    expect(readObjects(current().doc)).toEqual([])
    await userEvent.click(screen.getByRole('button', { name: 'Rückgängig' }))
    expect(readObjects(current().doc)).toHaveLength(1)
  })

  it('inserts a template as one undo step and offers it on an empty board', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }) })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)
    await connect(true)

    await userEvent.click(screen.getByRole('button', { name: 'Mit einer Vorlage starten' }))
    const picker = screen.getByRole('region', { name: 'Vorlage einfügen' })
    expect(within(picker).getAllByRole('button', { name: /^(?!Schließen)/ })).toHaveLength(10)
    await userEvent.click(within(picker).getByRole('button', { name: /^SWOT-Analyse/ }))

    expect(screen.queryByRole('region', { name: 'Vorlage einfügen' })).not.toBeInTheDocument()
    const list = within(screen.getByRole('region', { name: /Objekte/ }))
    expect(list.getByRole('button', { name: /^Rechteck: Stärken \(intern\)/ })).toBeInTheDocument()
    expect(readObjects(current().doc)).toHaveLength(5)

    // A second template goes to the right of what is already there.
    const right = Math.max(...readObjects(current().doc).map((o) => o.x + o.w))
    await userEvent.click(screen.getByRole('button', { name: 'Vorlagen' }))
    await userEvent.click(within(screen.getByRole('region', { name: 'Vorlage einfügen' })).getByRole('button', { name: /^Retrospektive/ }))
    const added = readObjects(current().doc).filter((o) => o.x >= right)
    expect(added).toHaveLength(7)

    await userEvent.click(screen.getByRole('button', { name: 'Rückgängig' }))
    expect(readObjects(current().doc)).toHaveLength(5)
    await userEvent.click(screen.getByRole('button', { name: 'Rückgängig' }))
    expect(readObjects(current().doc)).toEqual([])
  })

  it('shows changes and people from others live', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }) })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)
    await connect(true)

    const other = new Y.Doc()
    addObject(other, 'ellipse', { x: 50, y: 50 })
    act(() => Y.applyUpdate(current().doc, Y.encodeStateAsUpdate(other)))
    act(() => current().handlers.onPeer({ connectionId: 'c-2', userId: 'u-clara', name: 'Clara', x: 10, y: 20, selectedObjectId: null }))

    expect(screen.getByRole('button', { name: 'Ellipse (Grün)' })).toBeInTheDocument()
    expect(screen.getByText(/Gerade dabei: Ben, Clara/)).toBeInTheDocument()

    act(() => current().handlers.onPeerLeft('c-2'))
    expect(screen.getByText(/Gerade dabei: Ben$/)).toBeInTheDocument()
  })

  it('shows task cards with live task data', async () => {
    fakeApi({
      'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }),
      'GET /api/v1/whiteboards/b-1/tasks?ids=t-1,t-gone': () => json([{ id: 't-1', title: 'Startseite', status: 'in_progress', assigneeName: 'Clara', dueDate: null }]),
    })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)
    await connect(true)

    act(() => {
      addObject(current().doc, 'task', { x: 0, y: 0 }, 't-1')
      addObject(current().doc, 'task', { x: 0, y: 300 }, 't-gone')
    })

    expect(await screen.findByRole('button', { name: 'Aufgabe: Startseite' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Aufgabe: nicht verfügbar' })).toBeInTheDocument()
    expect(screen.getByText('In Arbeit')).toBeInTheDocument()
  })

  it('lets viewers look but not change anything', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }) })
    render(<WhiteboardPanel project={project(viewerRights)} me={ben} revision={0} onChanged={() => {}} />)
    await connect(false)
    act(() => void addObject(current().doc, 'sticky', { x: 0, y: 0 }))

    expect(screen.queryByRole('button', { name: '+ Notiz' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Vorlagen' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Neues Whiteboard')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Umbenennen' })).not.toBeInTheDocument()
    expect(screen.getByText(/Nur ansehen/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Notiz: Neue Notiz' }))
    expect(screen.getByLabelText('Text')).toBeDisabled()
    screen.getByRole('application').focus()
    await userEvent.keyboard('{Delete}')
    expect(readObjects(current().doc)).toHaveLength(1)
  })

  it('starts over with a fresh document when the server refuses a change', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/whiteboards?limit=100': () => json({ items: [board('b-1', 'Ideen')], nextOffset: null }) })
    render(<WhiteboardPanel project={project()} me={ben} revision={0} onChanged={() => {}} />)
    await connect(true)
    const first = current()

    act(() => first.handlers.onRejected('Forbidden'))

    expect(screen.getByRole('alert')).toHaveTextContent('nicht gespeichert')
    expect(first.stopped).toBe(true)
    expect(current()).not.toBe(first)
  })
})
