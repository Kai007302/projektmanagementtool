import { type APIRequestContext, type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// Phase 5 acceptance: plan tasks on the timeline, link them, add a milestone; viewers follow along live.

const eva = '01920000-0000-7000-8000-000000000105'

/** A date relative to today as yyyy-mm-dd, the format of the API and of date inputs. */
function day(offset: number): string {
  const date = new Date()
  date.setDate(date.getDate() + offset)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

async function api<T>(request: APIRequestContext, method: 'GET' | 'POST', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': 'dev-ben' } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

type Task = { id: string; title: string; startDate: string | null; dueDate: string | null }

async function openGantt(page: Page, user: string, project: string) {
  await signInAs(page, user, 'Projekte')
  await page.getByRole('button', { name: project }).click()
  await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Gantt' }).click()
  await expect(page.getByRole('heading', { name: 'Gantt', exact: true })).toBeVisible()
}

const bar = (page: Page, title: string) => page.getByRole('button', { name: new RegExp(`^${title}: `) })

test('tasks are planned, linked and moved on the Gantt chart while a viewer watches', async ({ browser, request }) => {
  const name = uniqueTitle('E2E Gantt')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: eva, role: 'viewer' })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Analyse', startDate: day(0), dueDate: day(3) })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Umsetzung' })
  const tasks = async () => (await api<{ tasks: Task[] }>(request, 'GET', `/projects/${project.id}/gantt`)).tasks
  const implementation = async () => (await tasks()).find((t) => t.title === 'Umsetzung')!

  // Eva (viewer) opens the chart first and sees changes arrive without reloading.
  const viewer = await browser.newPage()
  await openGantt(viewer, 'dev-eva', name)
  await expect(bar(viewer, 'Analyse')).toBeVisible()
  await expect(viewer.getByRole('button', { name: 'Termin speichern' })).toHaveCount(0)
  await expect(viewer.getByRole('form', { name: 'Neuer Meilenstein' })).toHaveCount(0)

  const ben = await browser.newPage()
  await openGantt(ben, 'dev-ben', name)

  // An unscheduled task is planned with the date form.
  await ben.getByRole('list', { name: 'Aufgaben' }).getByRole('button', { name: 'Umsetzung' }).click()
  const schedule = ben.getByRole('form', { name: 'Termin' })
  await schedule.getByLabel('Start').fill(day(2))
  await schedule.getByLabel('Ende').fill(day(6))
  // The details remount with the reloaded task version, so wait for the reload before using them again.
  const rescheduled = ben.waitForResponse((r) => r.request().method() === 'GET' && r.url().endsWith(`/projects/${project.id}/gantt`))
  await schedule.getByRole('button', { name: 'Termin speichern' }).click()
  await expect(bar(ben, 'Umsetzung')).toBeVisible()
  await expect.poll(async () => (await implementation()).startDate).toBe(day(2))
  await rescheduled

  // Linking it after "Analyse" flags the overlap; dependencies only warn.
  const link = ben.getByRole('form', { name: 'Vorgänger hinzufügen' })
  await link.getByLabel('Vorgänger').selectOption({ label: 'Analyse' })
  await link.getByRole('button', { name: 'Vorgänger hinzufügen' }).click()
  await expect(ben.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).toContainText('„Umsetzung“ passt nicht zu „Analyse“')
  await expect(viewer.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).toBeVisible()

  // Moving the bar with the keyboard resolves it: two days later it starts after "Analyse" ends.
  // The next key press needs the reloaded chart (new task version), not just the saved dates.
  const reloaded = ben.waitForResponse((r) => r.request().method() === 'GET' && r.url().endsWith(`/projects/${project.id}/gantt`))
  await bar(ben, 'Umsetzung').focus()
  await ben.keyboard.press('ArrowRight')
  await expect.poll(async () => (await implementation()).startDate).toBe(day(3))
  await reloaded
  await bar(ben, 'Umsetzung').focus()
  await ben.keyboard.press('ArrowRight')
  await expect.poll(async () => (await implementation()).startDate).toBe(day(4))
  await expect(ben.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).toHaveCount(0)
  await expect(viewer.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).toHaveCount(0)

  // Dragging the end handle of "Analyse" one day to the right breaks the link again.
  const analysis = bar(ben, 'Analyse')
  const handle = analysis.locator('[data-handle="end"]')
  const box = (await handle.boundingBox())!
  await ben.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await ben.mouse.down()
  await ben.mouse.move(box.x + box.width / 2 + 20, box.y + box.height / 2, { steps: 4 })
  await ben.mouse.move(box.x + box.width / 2 + 32, box.y + box.height / 2, { steps: 4 })
  await ben.mouse.up()
  await expect.poll(async () => (await tasks()).find((t) => t.title === 'Analyse')?.dueDate).toBe(day(4))
  await expect(viewer.getByRole('region', { name: 'Verletzte Abhängigkeiten' })).toBeVisible()

  // A milestone shows up for everyone.
  const milestone = ben.getByRole('form', { name: 'Neuer Meilenstein' })
  await milestone.getByLabel('Neuer Meilenstein').fill('Abnahme')
  await milestone.getByLabel('Datum').fill(day(10))
  await milestone.getByRole('button', { name: 'Meilenstein anlegen' }).click()
  await expect(viewer.getByRole('region', { name: 'Meilensteine' })).toContainText('Abnahme')

  await expectAlertFree(ben)
  await expectAlertFree(viewer)
})
