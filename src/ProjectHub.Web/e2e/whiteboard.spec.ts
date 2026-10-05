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

test('two people draw together, a viewer follows and the board survives a reload', async ({ browser, request }) => {
  const name = uniqueTitle('E2E Whiteboard')
  const task = uniqueTitle('Navigation skizzieren')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: clara, role: 'editor' })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: eva, role: 'viewer' })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: task })

  const ben = await openWhiteboards(browser, 'dev-ben', name)
  await ben.getByLabel('Neues Whiteboard').fill('Workshop')
  await ben.getByRole('button', { name: 'Anlegen', exact: true }).click()
  await expect(ben.getByRole('heading', { name: 'Workshop' })).toBeVisible()
  await expect(ben.getByText(/^Verbunden/)).toBeVisible()

  const claraPage = await openWhiteboards(browser, 'dev-clara', name)
  const evaPage = await openWhiteboards(browser, 'dev-eva', name)
  await expect(claraPage.getByText(/^Verbunden · Gerade dabei: .*Ben Projektleiter/)).toBeVisible()
  await expect(evaPage.getByText(/Nur ansehen/)).toBeVisible()
  await expect(evaPage.getByRole('button', { name: '+ Notiz' })).toHaveCount(0)

  // Ben writes a note; Clara and Eva see it without reloading.
  await ben.getByRole('button', { name: '+ Notiz' }).click()
  await ben.getByLabel('Text').fill('Suche nach oben')
  await expect(objectList(claraPage).getByRole('button', { name: 'Notiz: Suche nach oben' })).toBeVisible()
  await expect(objectList(evaPage).getByRole('button', { name: 'Notiz: Suche nach oben' })).toBeVisible()

  // Clara moves it with the form while Ben adds a task card; both changes arrive everywhere.
  await objectList(claraPage).getByRole('button', { name: 'Notiz: Suche nach oben' }).click()
  await claraPage.getByLabel('X', { exact: true }).fill('400')
  await ben.getByRole('combobox', { name: 'Aufgabe für eine Karte' }).focus()
  await ben.getByRole('combobox', { name: 'Aufgabe für eine Karte' }).selectOption({ label: task })
  await ben.getByRole('button', { name: '+ Aufgabe' }).click()
  await expect(objectList(evaPage).getByRole('button', { name: `Aufgabe: ${task}` })).toBeVisible()
  await objectList(ben).getByRole('button', { name: 'Notiz: Suche nach oben' }).click()
  await expect(ben.getByLabel('X', { exact: true })).toHaveValue('400')

  // Ben starts a retrospective from a template; it arrives for the others as a whole.
  await ben.getByRole('button', { name: 'Vorlagen' }).click()
  await ben.getByRole('region', { name: 'Vorlage einfügen' }).getByRole('button', { name: /^Retrospektive/ }).click()
  await expect(objectList(claraPage).getByRole('button', { name: 'Rechteck: Was lief gut?' })).toBeVisible()
  await expect(objectList(evaPage).getByRole('button', { name: 'Notiz: Daily auf 15 Minuten begrenzen' })).toBeVisible()

  // The viewer cannot change the note.
  await objectList(evaPage).getByRole('button', { name: 'Notiz: Suche nach oben' }).click()
  await expect(evaPage.getByLabel('Text')).toBeDisabled()

  // After a reload everything is still there.
  await claraPage.reload()
  await claraPage.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: 'Projekte', exact: true }).click()
  await claraPage.getByRole('button', { name }).click()
  await claraPage.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Whiteboard' }).click()
  await expect(objectList(claraPage).getByRole('button', { name: 'Notiz: Suche nach oben' })).toBeVisible()
  await expect(objectList(claraPage).getByRole('button', { name: `Aufgabe: ${task}` })).toBeVisible()

  // Ben renames and deletes the board; the others follow.
  await ben.getByRole('button', { name: 'Umbenennen' }).click()
  await ben.getByLabel('Name', { exact: true }).last().fill('Workshop März')
  await ben.getByRole('button', { name: 'Speichern' }).last().click()
  await expect(evaPage.getByRole('heading', { name: 'Workshop März' })).toBeVisible()
  ben.once('dialog', (dialog) => void dialog.accept())
  await ben.getByRole('button', { name: 'Löschen', exact: true }).click()
  await expect(evaPage.getByText('Dieses Projekt hat noch kein Whiteboard.')).toBeVisible()

  await expectAlertFree(ben)
  await expectAlertFree(evaPage)
})
