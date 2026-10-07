import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ProjectDetails } from '../projects/api'
import { fakeApi, json } from '../test/fakeApi'
import { Toaster } from '../ui/Toaster'
import { TaskTransfer } from './TaskTransfer'

function project(canContribute: boolean) {
  return { id: 'p-1', name: 'Intranet', capabilities: { canContribute, canEdit: false, canManage: false } } as unknown as ProjectDetails
}

const file = () => new File(['Titel;Status\nTexte;Offen\nBilder;\n'], 'aufgaben.csv', { type: 'text/csv' })

const imports = (api: ReturnType<typeof fakeApi>) => api.calls.filter((c) => c.key.startsWith('POST /api/v1/projects/p-1/tasks/import'))

describe('TaskTransfer', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('checks the file first and imports after confirmation', async () => {
    const api = fakeApi({
      'POST /api/v1/projects/p-1/tasks/import?dryRun=true': () => json({ rows: 2, created: 0, ignoredColumns: ['Kostenstelle'], errors: [] }),
      'POST /api/v1/projects/p-1/tasks/import?dryRun=false': () => json({ rows: 2, created: 2, ignoredColumns: [], errors: [] }),
    })
    const onImported = vi.fn()
    render(
      <>
        <TaskTransfer project={project(true)} onImported={onImported} />
        <Toaster />
      </>,
    )

    await userEvent.click(screen.getByRole('button', { name: '+ Aufgaben importieren' }))
    await userEvent.upload(screen.getByLabelText('Aufgaben importieren (Excel oder CSV)'), file())

    expect(await screen.findByText('2 Aufgaben erkannt. Nicht übernommen: Kostenstelle.')).toBeInTheDocument()
    expect(imports(api)).toHaveLength(1)
    expect(onImported).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: '2 Aufgaben importieren' }))

    expect(await screen.findByText('2 Aufgaben importiert. 🎉')).toBeInTheDocument()
    expect(onImported).toHaveBeenCalledOnce()
    expect(imports(api)[1].init?.body).toBeInstanceOf(FormData)
  })

  it('lists the problems in German and offers no import', async () => {
    fakeApi({
      'POST /api/v1/projects/p-1/tasks/import?dryRun=true': () =>
        json({
          rows: 2,
          created: 0,
          ignoredColumns: [],
          errors: [
            { row: 3, column: 'Status', code: 'unknown_status', message: "Unknown status 'Später'." },
            { row: 4, column: 'Zuständig', code: 'unknown_assignee', message: 'x' },
          ],
        }),
    })
    render(<TaskTransfer project={project(true)} onImported={() => {}} />)

    await userEvent.click(screen.getByRole('button', { name: '+ Aufgaben importieren' }))
    await userEvent.upload(screen.getByLabelText('Aufgaben importieren (Excel oder CSV)'), file())

    expect(await screen.findByText(/2 Probleme in der Datei/)).toBeInTheDocument()
    expect(screen.getByText('Zeile 3, Status: Status unbekannt (Offen, In Arbeit, Erledigt)')).toBeInTheDocument()
    expect(within(screen.getByRole('dialog')).queryByRole('button', { name: /importieren$/ })).not.toBeInTheDocument()
  })

  it('offers the export to everyone and the import only with write access', async () => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }))
    const api = fakeApi({ 'GET /api/v1/projects/p-1/tasks/export?format=xlsx': () => new Response('xlsx') })
    render(<TaskTransfer project={project(false)} onImported={() => {}} />)

    expect(screen.queryByRole('button', { name: '+ Aufgaben importieren' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Excel' }))

    expect(api.calls.some((c) => c.key === 'GET /api/v1/projects/p-1/tasks/export?format=xlsx')).toBe(true)
  })
})
