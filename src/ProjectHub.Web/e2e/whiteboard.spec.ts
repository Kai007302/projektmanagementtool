import { type APIRequestContext, type Browser, type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// Phase 7 acceptance: two people draw on the same whiteboard live, a viewer follows, content survives a reload.

const clara = '01920000-0000-7000-8000-000000000103'
const eva = '01920000-0000-7000-8000-000000000105'

async function api<T>(request: APIRequestContext, method: 'GET' | 'POST', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': 'dev-ben' } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

async function openWhiteboards(browser: Browser, user: string, project: string): Promise<Page> {
  const page = await (await browser.newContext()).newPage()
  await signInAs(page, user, 'Projekte')
  await page.getByRole('button', { name: project }).click()
  await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Whiteboard' }).click()
  return page
}

const objectList = (page: Page) => page.getByRole('region', { name: /^Objekte/ })

/** The object list is folded away like in Miro; it opens from the top right corner of the board. */
const showObjects = (page: Page) => page.getByRole('button', { name: /^Objekte \(/ }).click()

test('two people draw together, a viewer follows and the board survives a reload', async ({ browser, request }) => {
  const name = uniqueTitle('E2E Whiteboard')
  const task = uniqueTitle('Navigation skizzieren')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: clara, role: 'editor' })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: eva, role: 'viewer' })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: task })

  const ben = await openWhiteboards(browser, 'dev-ben', name)
  await ben.getByRole('button', { name: '+ Whiteboard' }).click()
  await ben.getByLabel('Neues Whiteboard').fill('Workshop')
  await ben.getByLabel('Neues Whiteboard').press('Enter')
  await expect(ben.getByRole('heading', { name: 'Workshop' })).toBeVisible()
  await expect(ben.getByText(/^Verbunden/)).toBeVisible()
  await showObjects(ben)

  const claraPage = await openWhiteboards(browser, 'dev-clara', name)
  const evaPage = await openWhiteboards(browser, 'dev-eva', name)
  await expect(claraPage.getByText(/^Verbunden · Gerade dabei: .*Ben Projektleiter/)).toBeVisible()
  await expect(evaPage.getByText(/Nur ansehen/)).toBeVisible()
  await showObjects(claraPage)
  await showObjects(evaPage)
  await expect(evaPage.getByRole('button', { name: 'Notiz hinzufügen' })).toHaveCount(0)

  // Ben writes a note; Clara and Eva see it without reloading.
  await ben.getByRole('button', { name: 'Notiz hinzufügen' }).click()
  await ben.getByLabel('Text', { exact: true }).fill('Suche nach oben')
  await expect(objectList(claraPage).getByRole('button', { name: 'Notiz: Suche nach oben' })).toBeVisible()
  await expect(objectList(evaPage).getByRole('button', { name: 'Notiz: Suche nach oben' })).toBeVisible()

  // Clara edits it with a double click while Ben adds a task card; both changes arrive everywhere.
  await claraPage.getByRole('application').locator('[data-object-id]', { hasText: 'Suche nach oben' }).dblclick()
  await claraPage.getByLabel('Text', { exact: true }).fill('Suche nach oben, bitte')
  await claraPage.keyboard.press('Escape')
  await ben.getByRole('button', { name: 'Aufgabe als Karte' }).click()
  await ben.getByRole('combobox', { name: 'Aufgabe für eine Karte' }).selectOption({ label: task })
  await ben.getByRole('button', { name: '+ Aufgabe' }).click()
  await expect(objectList(evaPage).getByRole('button', { name: `Aufgabe: ${task}` })).toBeVisible()
  await expect(objectList(ben).getByRole('button', { name: 'Notiz: Suche nach oben, bitte' })).toBeVisible()

  // Ben starts a retrospective from a template; it arrives for the others as a whole.
  await ben.getByRole('button', { name: 'Vorlagen' }).click()
  await ben.getByRole('region', { name: 'Vorlage einfügen' }).getByRole('button', { name: /^Retrospektive/ }).click()
  await expect(objectList(claraPage).getByRole('button', { name: 'Rechteck: Was lief gut?' })).toBeVisible()
  await expect(objectList(evaPage).getByRole('button', { name: 'Notiz: Daily auf 15 Minuten begrenzen' })).toBeVisible()

  // A note dragged from the sticky stack lands on the board for everyone.
  await ben.getByRole('button', { name: 'Notiz hinzufügen' }).dragTo(ben.getByRole('application'))
  await expect(objectList(claraPage).getByRole('button', { name: 'Notiz (Gelb)' })).toBeVisible()

  // The viewer cannot change the note.
  await evaPage.getByRole('application').locator('[data-object-id]', { hasText: 'Suche nach oben, bitte' }).dblclick()
  await expect(evaPage.getByLabel('Text', { exact: true })).toHaveCount(0)

  // After a reload everything is still there.
  await claraPage.reload()
  await claraPage.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: 'Projekte', exact: true }).click()
  await claraPage.getByRole('button', { name }).click()
  await claraPage.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Whiteboard' }).click()
  await showObjects(claraPage)
  await expect(objectList(claraPage).getByRole('button', { name: 'Notiz: Suche nach oben, bitte' })).toBeVisible()
  await expect(objectList(claraPage).getByRole('button', { name: `Aufgabe: ${task}` })).toBeVisible()

  // Ben renames and deletes the board; the others follow.
  await ben.getByRole('heading', { name: 'Workshop' }).getByRole('button').click()
  await ben.getByLabel('Name des Whiteboards', { exact: true }).fill('Workshop März')
  await ben.getByLabel('Name des Whiteboards', { exact: true }).press('Enter')
  await expect(evaPage.getByRole('heading', { name: 'Workshop März' })).toBeVisible()
  ben.once('dialog', (dialog) => void dialog.accept())
  await ben.getByRole('button', { name: 'Weitere Aktionen zum Whiteboard' }).click()
  await ben.getByRole('menuitem', { name: 'Whiteboard löschen' }).click()
  await expect(evaPage.getByText('Dieses Projekt hat noch kein Whiteboard.')).toBeVisible()

  await expectAlertFree(ben)
  await expectAlertFree(evaPage)
})
