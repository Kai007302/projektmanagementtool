import { expect, type Page } from '@playwright/test'

/** Signs in as one of the synthetic development users (DevelopmentSeedData) and opens a tab. */
export async function signInAs(page: Page, objectId: string, tab: 'Projekte' | 'Wissen' | 'Teams' = 'Wissen') {
  await page.goto('/')
  await page.evaluate((id) => localStorage.setItem('projecthub.devUser', id), objectId)
  await page.reload()
  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: tab, exact: true }).click()
}

export async function searchArticles(page: Page, text: string) {
  const search = page.getByRole('search')
  await search.getByLabel('Suche').fill(text)
  await search.getByRole('button', { name: 'Suchen' }).click()
}

export const uniqueTitle = (prefix: string) => `${prefix} ${Date.now().toString(36)}`

export async function expectAlertFree(page: Page) {
  await expect(page.getByRole('alert')).toHaveCount(0)
}
