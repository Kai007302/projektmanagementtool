import { expect, type Page } from '@playwright/test'

/** Content Security Policy violations the browser reported on any signed-in page; see fixtures.ts. */
export const cspViolations: string[] = []

/** Signs in as one of the synthetic development users (DevelopmentSeedData) and opens a tab. */
export async function signInAs(page: Page, objectId: string, tab: 'Projekte' | 'Wissen' | 'Verwaltung' | 'Assistent' = 'Wissen') {
  page.on('console', (message) => {
    if (message.type() === 'error' && message.text().includes('Content Security Policy')) cspViolations.push(message.text())
  })
  await page.goto('/')
  await page.evaluate((id) => localStorage.setItem('projecthub.devUser', id), objectId)
  await page.reload()
  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: tab, exact: true }).click()
}

export async function searchArticles(page: Page, text: string) {
  const search = page.getByRole('search')
  await search.getByLabel('Suche').fill(text)
  await search.getByLabel('Suche').press('Enter')
}

export const uniqueTitle = (prefix: string) => `${prefix} ${Date.now().toString(36)}`

export async function expectAlertFree(page: Page) {
  await expect(page.getByRole('alert')).toHaveCount(0)
}
