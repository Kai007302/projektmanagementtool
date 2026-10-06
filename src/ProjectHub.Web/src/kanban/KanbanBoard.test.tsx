import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { ProjectsPage } from '../projects/ProjectsPage'
import { useProjectEvents } from '../realtime/projectEvents'
import { fakeApi, json } from '../test/fakeApi'
import type { KanbanBoard as Board, KanbanCard } from './api'
import { KanbanBoard } from './KanbanBoard'

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
    members: [{ userId: 'u-ben', displayName: 'Ben', email: 'ben@x', role: 'admin' }],
  }
}

function card(id: string, title: string, overrides: Partial<KanbanCard> = {}): KanbanCard {
  return {
    id,
    title,
    status: 'todo',
    priority: 'normal',
    assigneeId: null,
    assigneeName: null,
    dueDate: null,
    progress: 0,
    subtaskCount: 0,
    version: 3,
    ...overrides,
  }
}

const design = card('t-1', 'Design')
const texts = card('t-2', 'Texte')
const deploy = card('t-3', 'Deployment', { status: 'in_progress' })

function board(): Board {
  return {
    id: 'b-1',
    projectId: 'p-1',
    name: 'Board',
    columns: [
      { id: 'c-todo', name: 'Offen', taskStatus: 'todo', wipLimit: null, version: 1, cards: [design, texts] },
      { id: 'c-doing', name: 'In Arbeit', taskStatus: 'in_progress', wipLimit: 1, version: 1, cards: [deploy] },
      { id: 'c-done', name: 'Erledigt', taskStatus: 'done', wipLimit: null, version: 1, cards: [] },
    ],
  }
}

const bodyOf = (api: ReturnType<typeof fakeApi>, key: string) =>
  JSON.parse(String(api.calls.find((c) => c.key === key)?.init?.body)) as unknown

function renderBoard(capabilities = all) {
  const onChanged = vi.fn()
  render(<KanbanBoard project={project(capabilities)} me={ben} revision={0} onChanged={onChanged} />)
  return onChanged
}

const column = (name: string) => within(screen.getByRole('region', { name }))

describe('KanbanBoard', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the columns with their cards and flags an exceeded WIP limit', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/board': () => json(board()) })
    renderBoard()

    await screen.findByRole('region', { name: 'Offen' })
    expect(column('Offen').getAllByRole('button', { name: /^(Design|Texte)$/ })).toHaveLength(2)
    expect(column('In Arbeit').getByText('1 / 1')).not.toHaveClass('over')
    expect(column('Erledigt').queryAllByRole('listitem')).toHaveLength(0)
  })

  it('moves a card to another column with its version', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'POST /api/v1/tasks/t-1/move': () => {
        const moved = board()
        moved.columns[0].cards = [texts]
        moved.columns[2].cards = [{ ...design, status: 'done', version: 4 }]
        return json(moved)
      },
    })
    const onChanged = renderBoard()

    await userEvent.click(await screen.findByRole('button', { name: 'Aktionen für „Design“' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Nach „Erledigt“ verschieben' }))

    expect(await column('Erledigt').findByRole('button', { name: 'Design' })).toBeInTheDocument()
    expect(bodyOf(api, 'POST /api/v1/tasks/t-1/move')).toEqual({ version: 3, columnId: 'c-done', index: 0 })
    expect(onChanged).toHaveBeenCalled()
  })

  it('drops a dragged card before the card it is dropped on', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'POST /api/v1/tasks/t-3/move': () => json(board()),
    })
    renderBoard()

    const source = (await screen.findByRole('button', { name: 'Deployment' })).closest('li')!
    const target = screen.getByRole('button', { name: 'Texte' }).closest('li')!
    fireEvent.dragStart(source, { dataTransfer: { setData: () => {} } })
    fireEvent.dragOver(target, { clientY: 0 })
    fireEvent.drop(target)

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/tasks/t-3/move')).toBe(true))
    expect(bodyOf(api, 'POST /api/v1/tasks/t-3/move')).toEqual({ version: 3, columnId: 'c-todo', index: 1 })
  })

  it('counts the index without the card when reordering within a column', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'POST /api/v1/tasks/t-1/move': () => json(board()),
    })
    renderBoard()

    const source = (await screen.findByRole('button', { name: 'Design' })).closest('li')!
    fireEvent.dragStart(source, { dataTransfer: { setData: () => {} } })
    fireEvent.dragOver(screen.getByRole('region', { name: 'Offen' }))
    fireEvent.drop(screen.getByRole('region', { name: 'Offen' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/tasks/t-1/move')).toBe(true))
    expect(bodyOf(api, 'POST /api/v1/tasks/t-1/move')).toEqual({ version: 3, columnId: 'c-todo', index: 1 })
  })

  it('reloads and explains when someone else changed the card first', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'POST /api/v1/tasks/t-1/move': () => json({ title: 'Conflict', detail: 'changed by someone else' }, 409),
    })
    renderBoard()

    await userEvent.click(await screen.findByRole('button', { name: 'Aktionen für „Design“' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Nach „Erledigt“ verschieben' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('inzwischen geändert')
    expect(await column('Offen').findByRole('button', { name: 'Design' })).toBeInTheDocument()
    expect(api.calls.filter((c) => c.key === 'GET /api/v1/projects/p-1/board')).toHaveLength(2)
  })

  it('shows viewers the board read-only', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/board': () => json(board()) })
    renderBoard(viewerRights)

    const cardItem = (await screen.findByRole('button', { name: 'Design' })).closest('li')!
    expect(cardItem).toHaveAttribute('draggable', 'false')
    expect(screen.queryByRole('button', { name: /^Aktionen für/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Spalte/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /bearbeiten/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Aufgabe$/ })).not.toBeInTheDocument()
  })

  it('opens a card for reading only, without fields to change it', async () => {
    fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'GET /api/v1/tasks/t-1': () =>
        json({ id: 't-1', projectId: 'p-1', title: 'Design', status: 'todo', priority: 'normal', assigneeId: null, assigneeName: null, dueDate: null, startDate: null, progress: 0, parentTaskId: null, version: 3 }),
      'GET /api/v1/tasks/t-1/comments?limit=100': () => json({ items: [], nextOffset: null }),
      'GET /api/v1/tasks/t-1/attachments': () => json([]),
      'GET /api/v1/tasks/t-1/whiteboards': () => json([]),
    })
    renderBoard()

    await userEvent.click(await screen.findByRole('button', { name: 'Design' }))

    expect(await screen.findByText('Status')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Status' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Titel bearbeiten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Weitere Aktionen zur Aufgabe' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Unteraufgabe' })).not.toBeInTheDocument()
    expect(screen.getByText(/nur zum Ansehen/)).toBeInTheDocument()
  })

  it('lets editors add a column', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'POST /api/v1/projects/p-1/board/columns': () => {
        const updated = board()
        updated.columns.push({ id: 'c-review', name: 'Review', taskStatus: 'in_progress', wipLimit: null, version: 1, cards: [] })
        return json(updated, 201)
      },
    })
    renderBoard()

    await userEvent.click(await screen.findByRole('button', { name: '+ Spalte' }))
    await userEvent.type(screen.getByLabelText('Name der neuen Spalte'), 'Review{Enter}')

    expect(await screen.findByRole('region', { name: 'Review' })).toBeInTheDocument()
    expect(bodyOf(api, 'POST /api/v1/projects/p-1/board/columns')).toEqual({ name: 'Review', taskStatus: 'in_progress' })
  })

  it('creates a task right in a column', async () => {
    const created = card('t-9', 'Review vorbereiten', { status: 'in_progress', version: 1 })
    let reloads = 0
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => {
        reloads++
        const current = board()
        if (reloads > 1) current.columns[1].cards.push(created)
        return json(current)
      },
      'POST /api/v1/projects/p-1/tasks': () => json(created, 201),
    })
    renderBoard()

    await screen.findByRole('region', { name: 'In Arbeit' })
    await userEvent.click(column('In Arbeit').getByRole('button', { name: '+ Aufgabe' }))
    await userEvent.type(column('In Arbeit').getByLabelText('Neue Aufgabe in In Arbeit'), 'Review vorbereiten{Enter}')

    expect(await column('In Arbeit').findByRole('button', { name: 'Review vorbereiten' })).toBeInTheDocument()
    expect(bodyOf(api, 'POST /api/v1/projects/p-1/tasks')).toEqual({ title: 'Review vorbereiten', parentTaskId: null, status: 'in_progress' })
    expect(column('In Arbeit').getByLabelText('Neue Aufgabe in In Arbeit')).toHaveValue('')
  })

  it('renames a column and sends only what changed', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'PATCH /api/v1/board-columns/c-done': () => json(board()),
    })
    renderBoard()

    await screen.findByRole('region', { name: 'Erledigt' })
    await userEvent.click(column('Erledigt').getByRole('button', { name: 'Erledigt' }))
    const name = column('Erledigt').getByLabelText('Spaltenname „Erledigt“')
    await userEvent.clear(name)
    await userEvent.type(name, 'Fertig{Enter}')

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/board-columns/c-done')).toBe(true))
    expect(bodyOf(api, 'PATCH /api/v1/board-columns/c-done')).toEqual({ version: 1, name: 'Fertig' })
  })

  it('explains why the last column of a status cannot be deleted', async () => {
    fakeApi({
      'GET /api/v1/projects/p-1/board': () => json(board()),
      'DELETE /api/v1/board-columns/c-done': () =>
        json({ title: 'Conflict', detail: 'Every task status needs at least one column on the board.' }, 409),
    })
    vi.stubGlobal('confirm', () => true)
    renderBoard()

    await userEvent.click(await screen.findByRole('button', { name: 'Spalte „Erledigt“' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Spalte löschen' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Jeder Status braucht mindestens eine Spalte.')
  })
})

describe('Realtime in the project view', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('reloads the board when someone else changes the project', async () => {
    const summary = { ...project(), myRole: 'admin' }
    const api = fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [summary], nextOffset: null }),
      'GET /api/v1/projects/p-1': () => json(project()),
      'GET /api/v1/projects/p-1/activity?limit=30': () => json({ items: [], nextOffset: null }),
      'GET /api/v1/users?limit=100': () => json({ items: [], nextOffset: null }),
      'GET /api/v1/projects/p-1/board': () => json(board()),
    })
    render(<ProjectsPage me={ben} />)
    await userEvent.click(await screen.findByRole('button', { name: /Intranet/ }))
    await screen.findByRole('region', { name: 'Offen' })
    const loads = () => api.calls.filter((c) => c.key === 'GET /api/v1/projects/p-1/board').length
    const before = loads()

    const [projectId, onChange] = vi.mocked(useProjectEvents).mock.lastCall!
    expect(projectId).toBe('p-1')
    act(() => onChange({ projectId: 'p-1', area: 'tasks' }))

    await vi.waitFor(() => expect(loads()).toBeGreaterThan(before))
  })
})
