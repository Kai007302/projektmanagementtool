import { expect, test, type APIRequestContext } from '@playwright/test'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

// Phase 6 acceptance: a notification arrives live, opens its task, and the mail setting is respected.

const clara = '01920000-0000-7000-8000-000000000103'

async function api<T>(request: APIRequestContext, user: string, method: 'GET' | 'POST' | 'PUT', path: string, data?: unknown): Promise<T> {
  const response = await request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-Dev-User': user } })
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true)
  return response.status() === 204 ? (undefined as T) : ((await response.json()) as T)
}

type Mail = { subject: string; body: string }

test('an assignment shows up live in the bell, opens the task and respects the mail setting', async ({ page, request }) => {
  const name = uniqueTitle('E2E Benachrichtigung')
  const task = uniqueTitle('Texte schreiben')
  const second = uniqueTitle('Bilder auswählen')
  const project = await api<{ id: string }>(request, 'dev-ben', 'POST', '/projects', { name })
  await api(request, 'dev-ben', 'POST', `/projects/${project.id}/members`, { userId: clara, role: 'editor' })

  try {
    await signInAs(page, 'dev-clara', 'Projekte')
    const bell = page.getByRole('button', { name: /^Benachrichtigungen, / })
    await bell.click()
    const panel = page.getByRole('region', { name: 'Benachrichtigungen' })
    await expect(panel.getByRole('button', { name: new RegExp(`^Ben Projektleiter hat dich zum Projekt „${name}“`) })).toBeVisible()
    await panel.getByRole('button', { name: 'Alle gelesen' }).click()
    await expect(bell).toHaveAccessibleName('Benachrichtigungen, keine ungelesen')
    await page.keyboard.press('Escape')

    // Ben assigns a task; Clara's bell counts up without reloading.
    await api(request, 'dev-ben', 'POST', `/projects/${project.id}/tasks`, { title: task, assigneeId: clara })
    await expect(bell).toHaveAccessibleName('Benachrichtigungen, 1 ungelesen')

    // Opening the notification jumps to the task in its project and marks it read.
    await bell.click()
    await panel.getByRole('button', { name: new RegExp(`^Ben Projektleiter hat dir „${task}“`) }).click()
    await expect(page.getByRole('heading', { name, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: task, exact: true })).toBeVisible()
    await expect(bell).toHaveAccessibleName('Benachrichtigungen, keine ungelesen')

    // With mails switched off, the next assignment only arrives in the app.
    await bell.click()
    const mail = panel.getByRole('checkbox', { name: 'Per Mail' })
    await expect(mail).toBeEnabled()
    await mail.uncheck()
    await expect(panel.getByText('Gespeichert.')).toBeVisible()
    await api(request, 'dev-ben', 'POST', `/projects/${project.id}/tasks`, { title: second, assigneeId: clara })
    await expect(bell).toHaveAccessibleName('Benachrichtigungen, 1 ungelesen')

    const outbox = await api<Mail[]>(request, 'dev-clara', 'GET', '/dev/outbox')
    expect(outbox.some((m) => m.subject.includes(task))).toBe(true)
    expect(outbox.some((m) => m.subject.includes(second))).toBe(false)
    expect(outbox.find((m) => m.subject.includes(task))?.body).not.toContain('Kommentar')
    await expectAlertFree(page)
  } finally {
    await api(request, 'dev-clara', 'PUT', '/me/notification-preferences', { inAppEnabled: true, emailEnabled: true })
  }
})
