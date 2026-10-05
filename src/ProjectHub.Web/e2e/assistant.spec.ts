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
