import AxeBuilder from '@axe-core/playwright'
import { type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { expectAlertFree, signInAs, uniqueTitle } from './helpers'

async function expectAccessible(page: Page) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([])
}

// ADR 0023: an experiment space shows its insights as cards with data, a time line and slides instead of a slide deck.
test('an experiment is written as a card with measurements and shown as slides', async ({ page }) => {
  const spaceName = uniqueTitle('E2E Labor')
  const title = uniqueTitle('E2E Wärme und Ausbeute')

  await signInAs(page, 'dev-ada')
  const headers = { 'X-Dev-User': 'dev-ada' }
  const me = await (await page.request.get('/api/v1/me', { headers })).json()
  const created = await page.request.post('/api/v1/knowledge/spaces', {
    headers,
    data: { name: spaceName, description: null, departmentId: me.departments[0].id, useCase: 'experiment' },
  })
  expect(created.ok()).toBe(true)
  await page.reload()
  await page.getByRole('navigation', { name: 'Wissen anzeigen als' }).getByRole('button', { name: 'Artikelliste' }).click()

  await page.getByRole('button', { name: spaceName, exact: true }).click()
  const insights = page.getByRole('region', { name: `Was wissen wir inzwischen über ${spaceName}?` })
  await expect(insights.getByText('Noch keine Erkenntnisse.', { exact: false })).toBeVisible()
  await insights.getByRole('button', { name: '+ Experiment' }).click()

  // The draft comes with the experiment's card; the measurements are pasted as from Excel.
  const editor = page.getByRole('form', { name: 'Artikel bearbeiten' })
  await editor.getByLabel('Titel').fill(title)
  await editor.getByLabel('Frage', { exact: true }).fill('Steigt die Ausbeute mit der Temperatur?')
  await editor.getByLabel('Erkenntnis', { exact: true }).fill('Ja, ab 40 °C deutlich.')
  await editor.getByLabel('Neuer Block').selectOption('data_table')
  await editor.getByRole('button', { name: 'Block hinzufügen' }).click()
  await editor.getByLabel('Titel', { exact: true }).nth(1).fill('Messreihe')
  await editor.getByLabel(/^Daten/).fill('Temperatur\tAusbeute\n20\t10\n40\t18\n60\t21')
  await expect(editor.getByRole('img', { name: /Liniendiagramm über Temperatur/ })).toBeVisible()
  await editor.getByRole('button', { name: 'Speichern' }).click()
  await expect(editor).toHaveCount(0)

  await expect(page.getByText('Ja, ab 40 °C deutlich.')).toBeVisible()
  await expect(page.getByRole('img', { name: /Ausbeute von 10 bis 21/ })).toBeVisible()
  await page.getByRole('combobox', { name: 'Ergebnis' }).selectOption({ label: 'Bestätigt' })
  await expectAlertFree(page)
  await expectAccessible(page)

  // Slides instead of a deck: the question, the data and the insight last.
  await page.getByRole('button', { name: 'Als Folien zeigen' }).click()
  const slides = page.getByRole('dialog', { name: `Folien: ${title}` })
  await expect(slides.getByRole('heading', { name: title })).toBeVisible()
  await expectAccessible(page)
  await page.keyboard.press('ArrowRight')
  await expect(slides.getByText('Steigt die Ausbeute mit der Temperatur?')).toBeVisible()
  await page.keyboard.press('End')
  await expect(slides.getByText('Ja, ab 40 °C deutlich.')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(slides).toHaveCount(0)

  // Questions about this article only; development answers with the deterministic model and names the article.
  await page.getByRole('button', { name: 'Fragen zur Doku' }).first().click()
  const chat = page.getByRole('complementary', { name: 'Fragen zur Doku' })
  await expect(chat.getByRole('combobox', { name: /Fragt in/ })).toHaveValue('article')
  await chat.getByRole('textbox', { name: 'Deine Frage' }).fill('Ab welcher Temperatur steigt die Ausbeute?')
  await chat.getByRole('textbox', { name: 'Deine Frage' }).press('Enter')
  const answer = chat.getByRole('article', { name: 'Antwort des Assistenten' })
  await expect(answer.getByRole('listitem').filter({ hasText: title })).toBeVisible()
  await expectAccessible(page)
  await chat.getByRole('button', { name: 'Fragen zur Doku schließen' }).click()

  // The space's time line shows the confirmed experiment.
  await page.getByRole('button', { name: '← Alle Artikel' }).click()
  await expect(insights.getByRole('button', { name: new RegExp(title) })).toContainText('Bestätigt')
})
