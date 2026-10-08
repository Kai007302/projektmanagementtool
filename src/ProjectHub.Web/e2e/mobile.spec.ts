import { type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { signInAs } from './helpers'

// Smartphone in portrait (docs/DESIGN.md, "Smartphone").
test.use({ viewport: { width: 390, height: 844 }, hasTouch: true })

/** The page itself never scrolls sideways; wide content scrolls inside its own box. */
async function expectNoSidewaysScroll(page: Page) {
  const widths = await page.evaluate(() => {
    // Measure without the safety clip on the app shell, so content that is too wide shows up here.
    const app = document.querySelector<HTMLElement>('.app')!
    app.style.overflowX = 'visible'
    const result = { page: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }
    app.style.overflowX = ''
    return result
  })
  expect(widths.page).toBeLessThanOrEqual(widths.viewport)
}

test('the pages fit the width of a phone', async ({ page }) => {
  await signInAs(page, 'dev-ben', 'Projekte')
  await expectNoSidewaysScroll(page)

  await page.locator('.project-card', { hasText: 'Intranet-Relaunch' }).first().click()
  const views = page.getByRole('navigation', { name: 'Ansicht', exact: true })
  for (const view of ['Board', 'Liste', 'Gantt', 'Whiteboard']) {
    await views.getByRole('button', { name: view, exact: true }).click()
    await expectNoSidewaysScroll(page)
  }

  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('button', { name: 'Galaxie', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Galaxie', level: 2 })).toBeVisible()
  await expectNoSidewaysScroll(page)
})

test('on a phone the article opens as a bottom sheet below the galaxy', async ({ page }) => {
  await signInAs(page, 'dev-eva', 'Galaxie')
  await page.getByRole('navigation', { name: 'Wissen anzeigen als' }).getByRole('button', { name: 'Galaxie' }).click()
  const canvas = page.getByRole('img', { name: /^Wissensgalaxie mit/ })
  await expect(canvas).toHaveAttribute('data-layout', 'done')
  await expectNoSidewaysScroll(page)

  await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Deployment-Prozess' })
  await expect(canvas).toHaveAttribute('data-view', 'idle')
  await canvas.press('Enter')

  // The sheet lies over the lower half of the screen; the galaxy is scrolled up and stays visible above it.
  const sheet = page.locator('.galaxy-popup.sheet')
  await expect(sheet.getByRole('heading', { name: 'Deployment-Prozess' })).toBeVisible()
  await expect.poll(async () => (await canvas.boundingBox())!.y).toBeLessThan(20)
  const sheetBox = (await sheet.boundingBox())!
  expect(sheetBox.y).toBeGreaterThan(844 * 0.4)
  expect(sheetBox.y + sheetBox.height).toBeCloseTo(844, 0)

  // Tapping the handle shows more of the article, tapping again less.
  const handle = sheet.getByRole('button', { name: 'Artikel vergrößern' })
  await handle.click()
  await expect(sheet.getByRole('button', { name: 'Artikel verkleinern' })).toHaveAttribute('aria-expanded', 'true')
  await expect.poll(async () => (await sheet.boundingBox())!.y).toBeLessThan(844 * 0.2)
  await sheet.getByRole('button', { name: 'Artikel verkleinern' }).click()
  await expect.poll(async () => (await sheet.boundingBox())!.y).toBeGreaterThan(844 * 0.49)

  // Pulling the handle down closes the article.
  const handleBox = (await sheet.getByRole('button', { name: 'Artikel vergrößern' }).boundingBox())!
  await page.mouse.move(handleBox.x + handleBox.width / 2, handleBox.y + handleBox.height / 2)
  await page.mouse.down()
  await page.mouse.move(handleBox.x + handleBox.width / 2, handleBox.y + 200, { steps: 6 })
  await page.mouse.up()
  await expect(sheet).toHaveCount(0)
})
