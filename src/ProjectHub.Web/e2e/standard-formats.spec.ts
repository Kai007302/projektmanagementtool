import { readFile } from 'node:fs/promises'
import { type APIRequestContext } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// ADR 0018: tasks as Excel/CSV, the Gantt chart as PDF and the person's dates as calendar subscription.

// David: the other tests sign in as Ben a lot, and the rate limit counts per person and minute.
const david = '01920000-0000-7000-8000-000000000104'

async function api<T>(request: APIRequestContext, method: 'GET' | 'POST', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': 'dev-ben' } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

async function openProject(page: import('@playwright/test').Page, name: string, view: 'Liste' | 'Gantt') {
  await signInAs(page, 'dev-ben', 'Projekte')
  await page.getByRole('button', { name }).click()
  await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: view }).click()
}

test('tasks are imported from a CSV file after a check and exported as Excel workbook', async ({ page, request }) => {
  const name = uniqueTitle('E2E Import')
  await api(request, 'POST', '/projects', { name })
  await openProject(page, name, 'Liste')

  await page.getByRole('button', { name: '+ Aufgaben importieren' }).click()
  await page.getByLabel('Aufgaben importieren (Excel oder CSV)').setInputFiles({
    name: 'aufgaben.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('Titel;Status;Priorität;Fällig\nAngebot prüfen;In Arbeit;Hoch;30.11.2026\nVertrag senden;;;\n', 'utf8'),
  })
  await expect(page.getByText('2 Aufgaben erkannt.')).toBeVisible()
  await page.getByRole('button', { name: '2 Aufgaben importieren' }).click()
  await expect(page.getByText('2 Aufgaben importiert. 🎉')).toBeVisible()
  const list = page.getByRole('region', { name: 'Aufgaben' })
  await expect(list.getByRole('button', { name: /Angebot prüfen/ })).toBeVisible()
  await expect(list.getByRole('button', { name: /Vertrag senden/ })).toBeVisible()

  const download = page.waitForEvent('download')
  await page.getByRole('group', { name: 'Aufgaben exportieren' }).getByRole('button', { name: 'Excel' }).click()
  const file = await download
  expect(file.suggestedFilename()).toMatch(/Aufgaben \d{4}-\d{2}-\d{2}\.xlsx$/)
  expect((await readFile((await file.path())!)).subarray(0, 2).toString('latin1')).toBe('PK')
  await expectAlertFree(page)
})

test('the Gantt chart downloads as PDF', async ({ page, request }) => {
  const name = uniqueTitle('E2E Gantt PDF')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Konzept', startDate: '2026-11-02', dueDate: '2026-11-06' })
  await openProject(page, name, 'Gantt')

  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Als PDF' }).click()
  const file = await download
  expect(file.suggestedFilename()).toMatch(/Gantt \d{4}-\d{2}-\d{2}\.pdf$/)
  expect((await readFile((await file.path())!)).subarray(0, 5).toString('latin1')).toBe('%PDF-')
  await expectAlertFree(page)
})

test('a person subscribes to their dates and the address works without signing in', async ({ page, request, playwright }) => {
  const name = uniqueTitle('E2E Kalender-Abo')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: david, role: 'member' })
  const due = new Date(Date.now() + 5 * 86_400_000).toISOString().slice(0, 10)
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Abo-Termin', assigneeIds: [david], dueDate: due })

  await signInAs(page, 'dev-david', 'Projekte')
  await page.getByRole('button', { name: /^Konto von / }).click()
  await page.getByRole('menuitem', { name: 'Meine Termine abonnieren' }).click()
  const panel = page.getByRole('dialog', { name: 'Meine Termine abonnieren' })
  const create = panel.getByRole('button', { name: 'Kalender-Adresse erstellen' })
  const replace = panel.getByRole('button', { name: 'Neue Adresse erstellen' })
  await expect(create.or(replace)).toBeVisible()
  if (await replace.isVisible()) {
    page.once('dialog', (dialog) => void dialog.accept())
    await replace.click()
  } else {
    await create.click()
  }

  const url = new URL(await panel.getByLabel('Deine Kalender-Adresse').inputValue())
  const anonymous = await playwright.request.newContext()
  const feed = await anonymous.get(new URL(url.pathname + url.search, page.url()).toString())
  expect(feed.status()).toBe(200)
  expect(await feed.text()).toContain(`SUMMARY:Abo-Termin · ${name}`)
  await anonymous.dispose()
  await expectAlertFree(page)
})
