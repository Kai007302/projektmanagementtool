import AxeBuilder from '@axe-core/playwright'
import { type APIRequestContext, type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { searchArticles, signInAs, uniqueTitle } from './helpers'

// Phase 10b: the main views have no serious or critical accessibility violations (WCAG 2.1 A and AA rules of axe).

const eva = '01920000-0000-7000-8000-000000000105'

async function api<T>(request: APIRequestContext, method: 'GET' | 'POST', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': 'dev-ben' } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

function day(offset: number): string {
  const date = new Date()
  date.setDate(date.getDate() + offset)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** Scans the whole page; a violation lists the rule and the offending elements so it can be found. */
async function expectAccessible(page: Page) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  const serious = violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id} (${v.impact}): ${v.help} — ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)
  expect(serious).toEqual([])
}

let project: { id: string; name: string }

test.beforeAll(async ({ request }) => {
  const name = uniqueTitle('E2E Barrierefreiheit')
  project = { ...(await api<{ id: string }>(request, 'POST', '/projects', { name })), name }
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: eva, role: 'viewer' })
  const analysis = await api<{ id: string }>(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Analyse', startDate: day(0), dueDate: day(3) })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Umsetzung', startDate: day(4), dueDate: day(8), parentTaskId: analysis.id })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Ohne Termin' })
  await api(request, 'POST', `/projects/${project.id}/whiteboards`, { name: 'Skizze' })
})

async function openProject(page: Page, view: 'Board' | 'Liste' | 'Gantt' | 'Whiteboard', user = 'dev-ben') {
  await signInAs(page, user, 'Projekte')
  await expectAccessible(page)
  await page.getByRole('button', { name: project.name }).click()
  await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: view, exact: true }).click()
}

test('project list and Kanban board', async ({ page }) => {
  await openProject(page, 'Board')
  await expect(page.getByRole('heading', { name: 'Board' })).toBeVisible()
  await expect(page.getByText('Analyse').first()).toBeVisible()
  await expectAccessible(page)
})

test('task list with an open task', async ({ page }) => {
  await openProject(page, 'Liste')
  await page.getByRole('button', { name: 'Analyse' }).first().click()
  await expectAccessible(page)
})

test('Gantt chart, also read-only for a viewer', async ({ page }) => {
  await openProject(page, 'Gantt')
  await expect(page.getByRole('heading', { name: 'Gantt', exact: true })).toBeVisible()
  await expectAccessible(page)
  await openProject(page, 'Gantt', 'dev-eva')
  await expect(page.getByRole('heading', { name: 'Gantt', exact: true })).toBeVisible()
  await expectAccessible(page)
})

test('whiteboard', async ({ page }) => {
  await openProject(page, 'Whiteboard')
  await expect(page.getByRole('heading', { name: 'Skizze' })).toBeVisible()
  await expect(page.getByText(/^Verbunden/)).toBeVisible()
  await expectAccessible(page)

  await page.getByRole('button', { name: 'Vorlagen' }).click()
  await expect(page.getByRole('region', { name: 'Vorlage einfügen' })).toBeVisible()
  await expectAccessible(page)

  await page.getByRole('button', { name: /^Notizfarbe und mehrere Notizen/ }).click()
  await expect(page.getByRole('group', { name: 'Notizfarbe' })).toBeVisible()
  await expectAccessible(page)
})

test('knowledge list, galaxy and an article', async ({ page }) => {
  await signInAs(page, 'dev-ada', 'Wissen')
  const views = page.getByRole('navigation', { name: 'Wissen anzeigen als' })
  await searchArticles(page, 'Deployment-Prozess')
  const article = page.getByRole('button', { name: /^Deployment-Prozess/ }).first()
  await expect(article).toBeVisible()
  await expectAccessible(page)

  await views.getByRole('button', { name: 'Galaxie' }).click()
  await expect(page.getByRole('img', { name: /^Wissensgalaxie/ })).toHaveAttribute('data-layout', 'done')
  await expectAccessible(page)

  await views.getByRole('button', { name: 'Artikel' }).click()
  await searchArticles(page, 'Deployment-Prozess')
  await article.click()
  await expect(page.getByRole('heading', { name: 'Deployment-Prozess', level: 2 })).toBeVisible()
  await expectAccessible(page)
})

test('notifications and teams', async ({ page }) => {
  await signInAs(page, 'dev-ben', 'Teams')
  await expectAccessible(page)
  await page.getByRole('button', { name: /^Benachrichtigungen, / }).click()
  await expect(page.getByRole('region', { name: 'Benachrichtigungen' })).toBeVisible()
  await expectAccessible(page)
})

test.describe('in dark mode', () => {
  test.use({ colorScheme: 'dark' })

  test('start page, Kanban board and knowledge keep their contrast', async ({ page }) => {
    await openProject(page, 'Board')
    await expect(page.getByText('Analyse').first()).toBeVisible()
    await expectAccessible(page)

    await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: 'Wissen', exact: true }).click()
    await searchArticles(page, 'Deployment-Prozess')
    await expect(page.getByRole('button', { name: /^Deployment-Prozess/ }).first()).toBeVisible()
    await expectAccessible(page)
  })
})
