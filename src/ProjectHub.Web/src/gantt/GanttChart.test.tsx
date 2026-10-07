import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { fakeApi, json } from '../test/fakeApi'
import type { Gantt, GanttTask } from './api'
import { GanttChart } from './GanttChart'

const ben: Me = { id: 'u-ben', displayName: 'Ben', email: 'ben@x', organizationId: 'org-1', organizationRole: 'member', departments: [] }

const all = { canContribute: true, canEdit: true, canManage: true }
const memberRights = { canContribute: true, canEdit: false, canManage: false }
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

function task(id: string, title: string, overrides: Partial<GanttTask> = {}): GanttTask {
  return {
    id,
    parentTaskId: null,
    title,
    status: 'todo',
    assigneeName: null,
    startDate: null,
    dueDate: null,
    progress: 0,
    version: 3,
    ...overrides,
  }
}

function gantt(): Gantt {
  return {
    projectId: 'p-1',
    tasks: [
      task('t-concept', 'Konzept', { startDate: '2026-11-02', dueDate: '2026-11-06', progress: 100, status: 'done' }),
      task('t-design', 'Design', { startDate: '2026-11-05', dueDate: '2026-11-12', progress: 40 }),
      task('t-start', 'Startseite', { parentTaskId: 't-design', dueDate: '2026-11-09' }),
      task('t-texts', 'Texte'),
    ],
    dependencies: [
      { id: 'd-1', sourceTaskId: 't-concept', targetTaskId: 't-design', dependencyType: 'finish_to_start', violated: true },
    ],
    milestones: [{ id: 'm-1', name: 'Go-live', date: '2026-11-20', version: 1 }],
  }
}

const bodyOf = (api: ReturnType<typeof fakeApi>, key: string) =>
  JSON.parse(String(api.calls.find((c) => c.key === key)?.init?.body)) as unknown

function renderChart(capabilities = all) {
  const onChanged = vi.fn()
  render(<GanttChart project={project(capabilities)} me={ben} revision={0} onChanged={onChanged} />)
  return onChanged
}

const bar = (title: string) => screen.findByRole('button', { name: new RegExp(`^${title}: `) })

describe('GanttChart', () => {
  beforeEach(() => {
    // jsdom has no pointer capture.
    Element.prototype.setPointerCapture = () => {}
  })
  afterEach(() => vi.unstubAllGlobals())

  it('shows tasks in outline order with their dates, milestones and broken dependencies', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/gantt': () => json(gantt()) })
    renderChart()

    expect(await bar('Design')).toHaveAccessibleName('Design: 5. Nov 2026 bis 12. Nov 2026, 40 % erledigt')
    expect(await bar('Startseite')).toHaveAccessibleName('Startseite: 9. Nov 2026, 0 % erledigt')
    const labels = within(screen.getByRole('list', { name: 'Aufgaben' })).getAllByRole('button')
    expect(labels.map((label) => label.textContent)).toEqual(['Konzept', 'Design', 'Startseite', 'Texte'])
    expect(screen.getByText('· ohne Termin')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Texte: / })).not.toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).getByText(/„Design“ passt nicht zu „Konzept“/)).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Meilensteine' })).getByText(/Go-live · 20. Nov 2026/)).toBeInTheDocument()
  })

  it('moves a bar with the arrow keys and changes only the end with shift', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'PATCH /api/v1/tasks/t-design': () => json({}),
    })
    const onChanged = renderChart()

    fireEvent.keyDown(await bar('Design'), { key: 'ArrowRight' })

    await vi.waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(bodyOf(api, 'PATCH /api/v1/tasks/t-design')).toEqual({ version: 3, startDate: '2026-11-06', dueDate: '2026-11-13' })

    api.calls.length = 0
    fireEvent.keyDown(await bar('Design'), { key: 'ArrowLeft', shiftKey: true })
    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/tasks/t-design')).toBe(true))
    expect(bodyOf(api, 'PATCH /api/v1/tasks/t-design')).toEqual({ version: 3, startDate: '2026-11-05', dueDate: '2026-11-11' })
  })

  it('drags a bar by whole days and resizes it at the end handle', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'PATCH /api/v1/tasks/t-concept': () => json({}),
    })
    renderChart()

    const concept = await bar('Konzept')
    fireEvent.pointerDown(concept, { button: 0, clientX: 100, pointerId: 1 })
    fireEvent.pointerMove(concept, { clientX: 166, pointerId: 1 })
    expect(concept).toHaveAccessibleName(/^Konzept: 4. Nov 2026 bis 8. Nov 2026/)
    fireEvent.pointerUp(concept, { clientX: 166, pointerId: 1 })

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/tasks/t-concept')).toBe(true))
    expect(bodyOf(api, 'PATCH /api/v1/tasks/t-concept')).toEqual({ version: 3, startDate: '2026-11-04', dueDate: '2026-11-08' })

    api.calls.length = 0
    const handle = (await bar('Design')).querySelector('[data-handle="end"]')!
    fireEvent.pointerDown(handle, { button: 0, clientX: 300, pointerId: 2 })
    fireEvent.pointerMove(handle, { clientX: 236, pointerId: 2 })
    fireEvent.pointerUp(handle, { clientX: 236, pointerId: 2 })
    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/tasks/t-design')).toBe(true))
    expect(bodyOf(api, 'PATCH /api/v1/tasks/t-design')).toEqual({ version: 3, startDate: '2026-11-05', dueDate: '2026-11-10' })
  })

  it('plans an unscheduled task with the date form', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'PATCH /api/v1/tasks/t-texts': () => json({}),
    })
    renderChart()

    await userEvent.click(await screen.findByRole('button', { name: 'Texte' }))
    const form = screen.getByRole('form', { name: 'Termin' })
    fireEvent.change(within(form).getByLabelText('Start'), { target: { value: '2026-11-16' } })
    fireEvent.change(within(form).getByLabelText('Ende'), { target: { value: '2026-11-18' } })
    await userEvent.click(within(form).getByRole('button', { name: 'Termin speichern' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'PATCH /api/v1/tasks/t-texts')).toBe(true))
    expect(bodyOf(api, 'PATCH /api/v1/tasks/t-texts')).toEqual({ version: 3, startDate: '2026-11-16', dueDate: '2026-11-18' })
  })

  it('adds a predecessor and explains a rejected cycle', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'POST /api/v1/projects/p-1/gantt/dependencies': () => json({ title: 'Conflict', detail: 'The dependency would create a cycle.' }, 409),
    })
    renderChart(memberRights)

    await userEvent.click(await screen.findByRole('button', { name: 'Konzept' }))
    const form = screen.getByRole('form', { name: 'Vorgänger hinzufügen' })
    await userEvent.selectOptions(within(form).getByLabelText('Vorgänger'), 't-design')
    await userEvent.selectOptions(within(form).getByLabelText('Art'), 'start_to_start')
    await userEvent.click(within(form).getByRole('button', { name: 'Vorgänger hinzufügen' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Diese Abhängigkeit würde einen Kreis bilden.')
    expect(bodyOf(api, 'POST /api/v1/projects/p-1/gantt/dependencies')).toEqual({
      sourceTaskId: 't-design',
      targetTaskId: 't-concept',
      dependencyType: 'start_to_start',
    })
    expect(screen.getByRole('button', { name: 'Abhängigkeit zu „Design“ entfernen' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Neuer Meilenstein' })).not.toBeInTheDocument()
  })

  it('lets editors manage milestones', async () => {
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'POST /api/v1/projects/p-1/gantt/milestones': () => json({ id: 'm-2', name: 'Abnahme', date: '2026-11-27', version: 1 }, 201),
      'PATCH /api/v1/gantt-milestones/m-1': () => json({}),
    })
    renderChart()

    const form = await screen.findByRole('form', { name: 'Neuer Meilenstein' })
    await userEvent.type(within(form).getByLabelText('Neuer Meilenstein'), 'Abnahme')
    fireEvent.change(within(form).getByLabelText('Datum'), { target: { value: '2026-11-27' } })
    await userEvent.click(within(form).getByRole('button', { name: 'Meilenstein anlegen' }))
    await vi.waitFor(() => expect(bodyOf(api, 'POST /api/v1/projects/p-1/gantt/milestones')).toEqual({ name: 'Abnahme', date: '2026-11-27' }))

    await userEvent.click(screen.getByRole('button', { name: 'Meilenstein „Go-live“ bearbeiten' }))
    const edit = screen.getByRole('form', { name: 'Meilenstein „Go-live“ bearbeiten' })
    fireEvent.change(within(edit).getByLabelText('Datum'), { target: { value: '2026-11-23' } })
    await userEvent.click(within(edit).getByRole('button', { name: 'Speichern' }))
    await vi.waitFor(() => expect(bodyOf(api, 'PATCH /api/v1/gantt-milestones/m-1')).toEqual({ version: 1, date: '2026-11-23' }))
  })

  it('shows viewers the schedule without anything to change', async () => {
    const api = fakeApi({ 'GET /api/v1/projects/p-1/gantt': () => json(gantt()) })
    renderChart(viewerRights)

    const design = await bar('Design')
    fireEvent.keyDown(design, { key: 'ArrowRight' })
    fireEvent.pointerDown(design, { button: 0, clientX: 0, pointerId: 1 })
    fireEvent.pointerMove(design, { clientX: 200, pointerId: 1 })
    fireEvent.pointerUp(design, { clientX: 200, pointerId: 1 })

    expect(await screen.findByText('Termin: 5. Nov 2026 bis 12. Nov 2026')).toBeInTheDocument()
    expect(screen.queryByRole('form')).not.toBeInTheDocument()
    expect(design.querySelector('[data-handle]')).toBeNull()
    expect(api.calls.filter((c) => c.key !== 'GET /api/v1/projects/p-1/gantt')).toHaveLength(0)
  })

  it('downloads the chart as PDF', async () => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }))
    const api = fakeApi({
      'GET /api/v1/projects/p-1/gantt': () => json(gantt()),
      'GET /api/v1/projects/p-1/gantt/export.pdf': () => new Response('%PDF-1.4', { headers: { 'Content-Type': 'application/pdf' } }),
    })
    renderChart(viewerRights)

    await userEvent.click(await screen.findByRole('button', { name: 'Als PDF' }))

    expect(api.calls.some((c) => c.key === 'GET /api/v1/projects/p-1/gantt/export.pdf')).toBe(true)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
