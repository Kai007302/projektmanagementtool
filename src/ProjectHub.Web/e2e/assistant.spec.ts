import AxeBuilder from '@axe-core/playwright'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs } from './helpers'

// ADR 0015: the assistant looks the question up in the knowledge the person may read, streams the answer and names
// its sources. Development uses the deterministic model (PROJECTHUB_AI_PROVIDER=fake), no language model is called.

test('the assistant answers from knowledge with sources that open the article', async ({ page }) => {
  await signInAs(page, 'dev-ben', 'Assistent')

  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

  await page.getByRole('textbox', { name: 'Deine Frage' }).fill('Wie läuft ein Kickoff ab?')
  await page.getByRole('button', { name: 'Fragen' }).click()

  const answer = page.getByRole('article', { name: 'Antwort des Assistenten' })
  await expect(answer).toContainText('Passend dazu')
  const source = answer.getByRole('listitem').filter({ hasText: 'Projekt-Kickoff durchführen' })
  await expect(source).toBeVisible()
  await expectAlertFree(page)

  await source.getByRole('button').click()
  await expect(page.getByRole('heading', { name: 'Projekt-Kickoff durchführen' })).toBeVisible()
})

// ADR 0016: a change the assistant proposes waits for the person; only "Ausführen" makes it happen.
test('a task the assistant proposes is created only after approval', async ({ page }) => {
  await signInAs(page, 'dev-ben', 'Assistent')
  const title = `Protokoll ${Date.now()}`

  await page.getByRole('combobox', { name: 'Bezug' }).selectOption({ label: 'Intranet-Relaunch' })
  await page.getByRole('textbox', { name: 'Deine Frage' }).fill(`Neue Aufgabe: ${title}`)
  await page.getByRole('button', { name: 'Fragen' }).click()

  const card = page.getByRole('region', { name: 'Vorschlag: Aufgabe anlegen' })
  await expect(card).toContainText('Intranet-Relaunch')
  await expect(card).toContainText(title)

  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

  await card.getByRole('button', { name: 'Ausführen' }).click()
  await expect(page.getByRole('article', { name: 'Antwort des Assistenten' })).toContainText('ist angelegt')
  await expect(card).toContainText('Freigegeben')
  await expectAlertFree(page)
})
