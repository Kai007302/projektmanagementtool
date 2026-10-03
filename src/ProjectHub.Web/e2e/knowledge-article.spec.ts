import { expect, test } from '@playwright/test'
import { expectAlertFree, searchArticles, signInAs, uniqueTitle } from './helpers'

// Phase 3 acceptance: create, edit and publish an article, with the visibility rules of DEC-020.
test('an article is written, reviewed and published, and only then visible to the organization', async ({ browser }) => {
  const title = uniqueTitle('E2E Onboarding-Leitfaden')

  // Clara writes a draft with several block types.
  const clara = await browser.newPage()
  await signInAs(clara, 'dev-clara')
  const create = clara.getByRole('region', { name: 'Neuer Artikel' })
  await create.getByLabel('Titel').fill(title)
  await create.getByLabel('Art').selectOption('how_to')
  await create.getByLabel('Bereich').selectOption({ label: 'Projektmethodik' })
  await create.getByRole('button', { name: 'Artikel anlegen' }).click()

  const editor = clara.getByRole('form', { name: 'Artikel bearbeiten' })
  await editor.getByLabel('Zusammenfassung').fill('Die ersten Tage im Projektteam.')
  await editor.getByLabel('Neuer Block').selectOption('heading')
  await editor.getByRole('button', { name: 'Block hinzufügen' }).click()
  await editor.getByLabel('Überschrift 1').fill('Erste Woche')
  await editor.getByLabel('Neuer Block').selectOption('bullet_list')
  await editor.getByRole('button', { name: 'Block hinzufügen' }).click()
  await editor.getByLabel(/Aufzählung 2/).fill('Zugänge beantragen\nTeam kennenlernen')
  await editor.getByLabel('Neuer Block').selectOption('project_reference')
  await editor.getByRole('button', { name: 'Block hinzufügen' }).click()
  await editor.getByLabel('Projekt 3').selectOption({ label: 'Intranet-Relaunch' })
  await editor.getByLabel('Änderungsnotiz').fill('Erste Fassung')
  await editor.getByRole('button', { name: 'Speichern' }).click()

  await expect(clara.getByRole('heading', { name: 'Erste Woche' })).toBeVisible()
  await expect(clara.getByText('Team kennenlernen')).toBeVisible()
  await expect(clara.locator('.reference-block').getByText('Intranet-Relaunch')).toBeVisible()
  await expectAlertFree(clara)

  // A second edit is a new version; the first stays in the history.
  await clara.getByRole('button', { name: 'Bearbeiten' }).click()
  await editor.getByLabel('Neuer Block').selectOption('callout')
  await editor.getByRole('button', { name: 'Block hinzufügen' }).click()
  await editor.getByLabel('Hinweis 4').fill('Fragen gerne im Team-Kanal stellen.')
  await editor.getByLabel('Änderungsnotiz').fill('Hinweis ergänzt')
  await editor.getByRole('button', { name: 'Speichern' }).click()
  const versions = clara.getByRole('region', { name: 'Versionen' })
  await expect(versions.getByText('Version 2')).toBeVisible()
  await expect(versions.getByText(/Hinweis ergänzt/)).toBeVisible()
  await expect(versions.getByText('Version 1')).toBeVisible()

  await clara.getByRole('button', { name: 'Zur Prüfung geben' }).click()
  await expect(clara.locator('.article-status')).toHaveText('In Prüfung')

  // Eva does not see the article while it is in review.
  const eva = await browser.newPage()
  await signInAs(eva, 'dev-eva')
  await searchArticles(eva, title)
  await expect(eva.getByText('Keine Artikel gefunden.')).toBeVisible()

  // Ada publishes it.
  const ada = await browser.newPage()
  await signInAs(ada, 'dev-ada')
  await searchArticles(ada, title)
  await ada.getByRole('button', { name: new RegExp(title) }).click()
  await ada.getByRole('button', { name: 'Veröffentlichen' }).click()
  await expect(ada.locator('.article-status')).toHaveText('Veröffentlicht')

  // Now Eva finds it by its content, reads it, but cannot change it.
  await signInAs(eva, 'dev-eva')
  await searchArticles(eva, 'Zugänge')
  await eva.getByRole('button', { name: new RegExp(title) }).click()
  await expect(eva.getByRole('heading', { name: title, level: 2 })).toBeVisible()
  await expect(eva.getByText('Fragen gerne im Team-Kanal stellen.')).toBeVisible()
  await expect(eva.getByRole('button', { name: 'Bearbeiten' })).toHaveCount(0)
  await expect(eva.getByRole('button', { name: 'Archivieren' })).toHaveCount(0)

  await eva.getByRole('textbox', { name: 'Kommentar' }).fill('Sehr hilfreich, danke!')
  await eva.getByRole('button', { name: 'Kommentieren' }).click()
  await expect(eva.locator('.comment').getByText('Sehr hilfreich, danke!')).toBeVisible()
})

test('a restricted article stays hidden from people without a grant', async ({ page }) => {
  await signInAs(page, 'dev-eva')
  await searchArticles(page, 'Rabattstaffeln')
  await expect(page.getByText('Keine Artikel gefunden.')).toBeVisible()

  await signInAs(page, 'dev-david')
  await searchArticles(page, 'Rabattstaffeln')
  await expect(page.getByRole('button', { name: /Preisliste Vertrieb 2027/ })).toBeVisible()
})
