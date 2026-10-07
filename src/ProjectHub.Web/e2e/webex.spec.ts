import { type APIRequestContext } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// Phase 9 acceptance: Webex meetings and the project space are linked, and notifications arrive per Webex (fake bot).

const clara = '01920000-0000-7000-8000-000000000103'

async function api<T>(request: APIRequestContext, user: string, method: 'GET' | 'POST' | 'PUT', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': user } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

type FakeWebex = { directMessages: { markdown: string }[]; spaces: { title: string; members: string[]; messages: string[] }[] }

test('an editor links a meeting and creates the project space; notifications arrive per Webex', async ({ page, request }) => {
  const name = uniqueTitle('E2E Webex')
  const task = uniqueTitle('Agenda vorbereiten')
  const project = await api<{ id: string }>(request, 'dev-ben', 'POST', '/projects', { name })
  await api(request, 'dev-ben', 'POST', `/projects/${project.id}/members`, { userId: clara, role: 'editor' })

  try {
    await signInAs(page, 'dev-clara', 'Projekte')

    // Clara switches on Webex notifications in the bell.
    await page.getByRole('button', { name: /^Benachrichtigungen, / }).click()
    const panel = page.getByRole('region', { name: 'Benachrichtigungen' })
    await panel.getByRole('checkbox', { name: 'Per Webex' }).check()
    await expect(panel.getByText('Gespeichert.')).toBeVisible()
    await page.keyboard.press('Escape')

    await page.getByRole('button', { name }).click()
    await page.getByRole('navigation', { name: 'Ansicht' }).getByRole('button', { name: 'Webex' }).click()
    const webex = page.getByRole('region', { name: 'Webex' })

    // A link that is not on webex.com is refused before it reaches the server.
    await webex.getByRole('button', { name: '+ Webex-Link' }).click()
    const form = page.getByRole('dialog', { name: 'Webex-Link hinzufügen' }).getByRole('form', { name: 'Webex-Link hinzufügen' })
    await form.getByLabel('Titel').fill('Jour fixe')
    await form.getByLabel('Link').fill('https://webex.com.evil.example/meet')
    await form.getByRole('button', { name: 'Hinzufügen' }).click()
    await expect(form.getByRole('alert')).toContainText('webex.com')
    await form.getByLabel('Link').fill('https://contoso.webex.com/meet/ben')
    await form.getByRole('button', { name: 'Hinzufügen' }).click()
    await expect(webex.getByRole('link', { name: 'Meeting „Jour fixe“ in Webex öffnen (neues Fenster)' })).toHaveAttribute('href', 'https://contoso.webex.com/meet/ben')

    // The bot creates the project space with the members; the button is gone afterwards.
    await webex.getByRole('button', { name: 'Projektraum in Webex anlegen' }).click()
    await expect(webex.getByRole('link', { name: `Space „${name}“ in Webex öffnen (neues Fenster)` })).toBeVisible()
    await expect(webex.getByRole('button', { name: 'Projektraum in Webex anlegen' })).toHaveCount(0)
    const fake = await api<FakeWebex>(request, 'dev-clara', 'GET', '/dev/webex')
    const space = fake.spaces.find((s) => s.title === name)
    expect(space?.members).toEqual(expect.arrayContaining(['ben@contoso-dev.example.invalid', 'clara@contoso-dev.example.invalid']))
    expect(space?.messages[0]).toContain('In ProjectHub öffnen')

    // Ben assigns a task: Clara gets a direct message from the bot.
    await api(request, 'dev-ben', 'POST', `/projects/${project.id}/tasks`, { title: task, assigneeId: clara })
    await expect
      .poll(async () => (await api<FakeWebex>(request, 'dev-clara', 'GET', '/dev/webex')).directMessages.some((m) => m.markdown.includes(task)))
      .toBe(true)
    await expectAlertFree(page)
  } finally {
    await api(request, 'dev-clara', 'PUT', '/me/notification-preferences', { inAppEnabled: true, emailEnabled: true, webexEnabled: false })
  }
})
