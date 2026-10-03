import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Me } from '../identity/api'
import { fakeApi, json } from '../test/fakeApi'
import { ProjectsPage } from './ProjectsPage'

const ben: Me = { id: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', organizationId: 'org-1', organizationRole: 'member' }
const eva: Me = { id: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', organizationId: 'org-1', organizationRole: 'member' }

const summary = {
  id: 'p-1',
  name: 'Intranet',
  description: null,
  status: 'active',
  startDate: null,
  endDate: null,
  myRole: 'admin',
  version: 1,
}

const members = [
  { userId: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', role: 'admin' },
  { userId: 'u-clara', displayName: 'Clara Editor', email: 'clara@x', role: 'editor' },
  { userId: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', role: 'viewer' },
]

const all = { canContribute: true, canEdit: true, canManage: true }
const none = { canContribute: false, canEdit: false, canManage: false }

const details = (capabilities = all) => ({ ...summary, ownerId: 'u-ben', capabilities, members })

function task(id: string, title: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    projectId: 'p-1',
    parentTaskId: null,
    title,
    description: null,
    status: 'todo',
    priority: 'normal',
    assigneeId: null,
    assigneeName: null,
    startDate: null,
    dueDate: null,
    progress: 0,
    estimatedHours: null,
    subtaskCount: 0,
    version: 1,
    ...overrides,
  }
}

const design = task('t-1', 'Design abstimmen', { subtaskCount: 1 })
const mockups = task('t-2', 'Mockups', { parentTaskId: 't-1' })
const empty = { items: [], nextOffset: null }

function projectRoutes(capabilities = all, tasks = [design, mockups]) {
  return {
    'GET /api/v1/projects?limit=100': () => json({ items: [summary], nextOffset: null }),
    'GET /api/v1/projects/p-1': () => json(details(capabilities)),
    'GET /api/v1/projects/p-1/activity?limit=30': () => json(empty),
    'GET /api/v1/projects/p-1/tasks?limit=100': () => json({ items: tasks, nextOffset: null }),
    'GET /api/v1/users?limit=100': () => json(empty),
    'GET /api/v1/tasks/t-1': () => json(design),
    'GET /api/v1/tasks/t-1/comments?limit=100': () => json(empty),
    'GET /api/v1/tasks/t-1/attachments': () => json([]),
  }
}

async function openProject() {
  await userEvent.click(await screen.findByRole('button', { name: /Intranet/ }))
  await userEvent.click(await screen.findByRole('button', { name: 'Liste' }))
  return within(await screen.findByRole('region', { name: 'Aufgaben' }))
}

describe('ProjectsPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists projects with the own role', async () => {
    fakeApi(projectRoutes())
    render(<ProjectsPage me={ben} />)

    expect(await screen.findByRole('button', { name: /Intranet.*Aktiv · Admin/ })).toBeInTheDocument()
  })

  it('creates a project and opens it', async () => {
    const api = fakeApi({
      ...projectRoutes(),
      'GET /api/v1/projects?limit=100': () => json(empty),
      'POST /api/v1/projects': () => json(summary, 201),
    })
    render(<ProjectsPage me={ben} />)

    await userEvent.type(await screen.findByLabelText('Neues Projekt'), 'Intranet')
    await userEvent.click(screen.getByRole('button', { name: 'Projekt anlegen' }))

    expect(await screen.findByRole('heading', { name: 'Intranet' })).toBeInTheDocument()
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/projects')?.init?.body))).toMatchObject({ name: 'Intranet' })
  })

  it('shows tasks with their subtasks', async () => {
    fakeApi(projectRoutes())
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    const parent = (await board.findByRole('button', { name: /Design abstimmen/ })).closest('li')!

    expect(within(parent).getByRole('button', { name: /Mockups/ })).toBeInTheDocument()
  })

  it('creates a task', async () => {
    let tasks = [design]
    const api = fakeApi({
      ...projectRoutes(all, tasks),
      'GET /api/v1/projects/p-1/tasks?limit=100': () => json({ items: tasks, nextOffset: null }),
      'POST /api/v1/projects/p-1/tasks': () => {
        const created = task('t-3', 'Texte schreiben')
        tasks = [...tasks, created]
        return json(created, 201)
      },
    })
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    await userEvent.type(board.getByLabelText('Neue Aufgabe'), 'Texte schreiben')
    await userEvent.click(board.getByRole('button', { name: 'Anlegen' }))

    expect(await board.findByRole('button', { name: /Texte schreiben/ })).toBeInTheDocument()
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/projects/p-1/tasks')?.init?.body))).toEqual({
      title: 'Texte schreiben',
      parentTaskId: null,
    })
  })

  it('shows viewers the tasks read-only', async () => {
    fakeApi(projectRoutes(none))
    render(<ProjectsPage me={eva} />)

    const board = await openProject()
    await userEvent.click(await board.findByRole('button', { name: /Design abstimmen/ }))
    await board.findByRole('heading', { name: 'Kommentare' })

    expect(board.queryByLabelText('Neue Aufgabe')).not.toBeInTheDocument()
    expect(board.queryByRole('button', { name: 'Aufgabe speichern' })).not.toBeInTheDocument()
    expect(board.queryByRole('button', { name: 'Aufgabe löschen' })).not.toBeInTheDocument()
    expect(board.queryByLabelText('Kommentar')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Projekt löschen' })).not.toBeInTheDocument()
  })

  it('sends only changed task fields with the version and offers only working members as assignees', async () => {
    const api = fakeApi({
      ...projectRoutes(),
      'PATCH /api/v1/tasks/t-1': () => json({ ...design, status: 'in_progress', version: 2 }),
    })
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    await userEvent.click(await board.findByRole('button', { name: /Design abstimmen/ }))
    const assignee = await board.findByLabelText('Zuständig')
    expect(within(assignee).queryByRole('option', { name: 'Eva Viewer' })).not.toBeInTheDocument()

    await userEvent.selectOptions(board.getByLabelText('Status'), 'in_progress')
    await userEvent.selectOptions(assignee, 'u-clara')
    await userEvent.click(board.getByRole('button', { name: 'Aufgabe speichern' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/tasks/t-1')).toBe(true))
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'PATCH /api/v1/tasks/t-1')?.init?.body))).toEqual({
      version: 1,
      status: 'in_progress',
      assigneeId: 'u-clara',
    })
  })

  it('reloads the task and explains a conflicting change', async () => {
    let current = design
    const api = fakeApi({
      ...projectRoutes(),
      'GET /api/v1/tasks/t-1': () => json(current),
      'PATCH /api/v1/tasks/t-1': () => {
        current = { ...design, title: 'Von Clara geändert', version: 2 }
        return json({ title: 'Conflict' }, 409)
      },
    })
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    await userEvent.click(await board.findByRole('button', { name: /Design abstimmen/ }))
    await userEvent.selectOptions(await board.findByLabelText('Priorität'), 'high')
    await userEvent.click(board.getByRole('button', { name: 'Aufgabe speichern' }))

    expect(await board.findByText(/inzwischen geändert/)).toBeInTheDocument()
    expect(await board.findByDisplayValue('Von Clara geändert')).toBeInTheDocument()
    expect(api.calls.filter((c) => c.key === 'GET /api/v1/tasks/t-1')).toHaveLength(2)
  })

  it('sends mentioned project members with a comment', async () => {
    const api = fakeApi({
      ...projectRoutes(),
      'POST /api/v1/tasks/t-1/comments': () =>
        json({ id: 'c-1', authorId: 'u-ben', authorName: 'Ben', content: 'x', createdAt: '2026-10-03T10:00:00Z', version: 1 }, 201),
    })
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    await userEvent.click(await board.findByRole('button', { name: /Design abstimmen/ }))
    await userEvent.type(await board.findByLabelText('Kommentar'), '@Clara Editor bitte prüfen')
    await userEvent.click(board.getByRole('button', { name: 'Kommentieren' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/tasks/t-1/comments')).toBe(true))
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/tasks/t-1/comments')?.init?.body))).toEqual({
      content: '@Clara Editor bitte prüfen',
      mentionedUserIds: ['u-clara'],
    })
  })

  it('uploads an attachment as form data', async () => {
    const api = fakeApi({
      ...projectRoutes(),
      'POST /api/v1/tasks/t-1/attachments': () =>
        json({ id: 'a-1', fileName: 'notiz.txt', sizeBytes: 5, uploadedBy: 'u-ben', uploadedByName: 'Ben', createdAt: '2026-10-03T10:00:00Z' }, 201),
    })
    render(<ProjectsPage me={ben} />)

    const board = await openProject()
    await userEvent.click(await board.findByRole('button', { name: /Design abstimmen/ }))
    await userEvent.upload(await board.findByLabelText('Datei'), new File(['hallo'], 'notiz.txt', { type: 'text/plain' }))
    await userEvent.click(board.getByRole('button', { name: 'Hochladen' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/tasks/t-1/attachments')).toBe(true))
    const call = api.calls.find((c) => c.key === 'POST /api/v1/tasks/t-1/attachments')!
    expect(call.init?.body).toBeInstanceOf(FormData)
    expect(call.headers.has('Content-Type')).toBe(false)
  })
})
