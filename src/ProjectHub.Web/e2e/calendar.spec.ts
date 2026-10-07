import { readFile } from 'node:fs/promises'
import { type APIRequestContext } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// Phase 8 acceptance: tasks and milestones go into the person's own calendar without Graph permissions.

const eva = '01920000-0000-7000-8000-000000000105'

async function api<T>(request: APIRequestContext, method: 'GET' | 'POST', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': 'dev-ben' } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

test('a viewer downloads a task and a milestone as calendar entries and opens Outlook', async ({ page, request }) => {
  const name = uniqueTitle('E2E Kalender')
  const project = await api<{ id: string }>(request, 'POST', '/projects', { name })
  await api(request, 'POST', `/projects/${project.id}/members`, { userId: eva, role: 'viewer' })
  await api(request, 'POST', `/projects/${project.id}/tasks`, { title: 'Abnahme, Teil 1', startDate: '2026-11-02', dueDate: '2026-11-04' })
  await api(request, 'POST', `/projects/${project.id}/gantt/milestones`, { name: 'Go-live', date: '2026-12-15' })

  await signInAs(page, 'dev-eva', 'Projekte')
  await page.getByRole('button', { name }).click()
  await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Gantt' }).click()
  await page.getByRole('list', { name: 'Aufgaben' }).getByRole('button', { name: 'Abnahme, Teil 1' }).click()
  await page.getByRole('button', { name: 'Details der Aufgabe anzeigen' }).click()

  const taskDownload = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Aufgabe „Abnahme, Teil 1“ als Kalenderdatei herunterladen' }).click()
  const taskFile = await taskDownload
  expect(taskFile.suggestedFilename()).toBe('Abnahme, Teil 1.ics')
  const taskText = await readFile((await taskFile.path())!, 'utf8')
  expect(taskText).toContain('SUMMARY:Abnahme\\, Teil 1\r\n')
  expect(taskText).toContain('DTSTART;VALUE=DATE:20261102\r\nDTEND;VALUE=DATE:20261105\r\n')

  const outlook = page.getByRole('link', { name: 'Aufgabe „Abnahme, Teil 1“ in Outlook im Web anlegen (neues Fenster)' })
  const href = new URL((await outlook.getAttribute('href'))!)
  expect(href.host).toBe('outlook.office.com')
  expect(href.searchParams.get('subject')).toBe('Abnahme, Teil 1')
  expect([href.searchParams.get('startdt'), href.searchParams.get('enddt')]).toEqual(['2026-11-02', '2026-11-05'])
  await expect(outlook).toHaveAttribute('target', '_blank')
  // The task opens in its own window over the page; Escape closes it again.
  await page.keyboard.press('Escape')

  const milestoneDownload = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Meilenstein „Go-live“ als Kalenderdatei herunterladen' }).click()
  const milestoneText = await readFile((await (await milestoneDownload).path())!, 'utf8')
  expect(milestoneText).toContain('SUMMARY:Go-live\r\n')
  expect(milestoneText).toContain('DTSTART;VALUE=DATE:20261215\r\n')
  await expectAlertFree(page)
})
