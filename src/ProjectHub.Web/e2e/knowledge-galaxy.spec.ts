import { type Page } from '@playwright/test'
import { expect, test } from './fixtures'
import { signInAs } from './helpers'

async function openGalaxy(page: Page) {
  await signInAs(page, 'dev-eva')
  await page.getByRole('navigation', { name: 'Wissen anzeigen als' }).getByRole('button', { name: 'Galaxie' }).click()
  const canvas = page.getByRole('img', { name: /^Wissensgalaxie mit \d+ Artikeln? und \d+ Beziehung(en)?$/ })
  await expect(canvas).toBeVisible()
  await expect(canvas).toHaveAttribute('data-layout', 'done')
  return canvas
}

/** Number of painted pixels: the galaxy is actually drawn, not just an empty canvas. */
const paintedPixels = (page: Page) =>
  page.evaluate(() => {
    const canvas = document.querySelector<HTMLCanvasElement>('.galaxy-canvas canvas')!
    const data = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data
    let painted = 0
    for (let i = 3; i < data.length; i += 4) if (data[i] > 0) painted++
    return painted
  })

// Phase 3 acceptance: navigate the Knowledge Galaxy with the mouse, the keyboard and the list alternative.
test('the galaxy is drawn and navigable with the mouse', async ({ page }) => {
  const canvas = await openGalaxy(page)
  await expect.poll(() => paintedPixels(page)).toBeGreaterThan(1000)

  // Focus an article, then follow one of its relations from the details panel.
  await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Deployment-Prozess' })
  const details = page.getByRole('region', { name: 'Deployment-Prozess' })
  await expect(details.getByText('Wie Änderungen von der Entwicklung in die Produktion gelangen.')).toBeVisible()
  await details.getByRole('button', { name: 'Störungen melden' }).click()
  await expect(page.getByRole('region', { name: 'Störungen melden' })).toBeVisible()

  // Clicking a planet opens its article right in the galaxy.
  await expect(canvas).toHaveAttribute('data-view', 'idle')
  const box = (await canvas.boundingBox())!
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  const popup = page.locator('.galaxy-popup')
  await expect(popup.getByRole('heading', { name: 'Störungen melden' })).toBeVisible()

  // Dragging pans, the wheel zooms; the drawing follows.
  await page.mouse.move(box.x + 50, box.y + 50)
  await page.mouse.down()
  await page.mouse.move(box.x + 250, box.y + 150, { steps: 5 })
  await page.mouse.up()
  await page.mouse.wheel(0, 400)
  await expect.poll(() => paintedPixels(page)).toBeGreaterThan(1000)

  // The full page is one more click.
  await popup.getByRole('button', { name: 'Ganze Seite öffnen' }).click()
  await expect(page.getByRole('heading', { name: 'Störungen melden', level: 2 })).toBeVisible()
})

test('clicking or zooming into a planet opens its article in the galaxy, the planet moves beside it', async ({ page }) => {
  const canvas = await openGalaxy(page)
  const popup = page.locator('.galaxy-popup')

  // Focus puts the planet in the middle; a click there opens it and the planet jumps to the left of the pop-up.
  await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Deployment-Prozess' })
  await expect(canvas).toHaveAttribute('data-view', 'idle')
  const box = (await canvas.boundingBox())!
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  await expect(popup.getByRole('heading', { name: 'Deployment-Prozess' })).toBeVisible()
  await expect(popup.getByRole('heading', { name: 'Verbunden mit' })).toBeVisible()
  await expect(canvas).toHaveAttribute('data-view', 'idle')
  const popupBox = (await popup.boundingBox())!
  expect(popupBox.x + popupBox.width).toBeGreaterThan(box.x + box.width - 40)
  await page.mouse.move((box.x + popupBox.x) / 2, box.y + box.height / 2)
  await expect(page.locator('.galaxy-tooltip')).toContainText('Deployment-Prozess')

  await popup.getByRole('button', { name: 'Artikel schließen' }).click()
  await expect(popup).toHaveCount(0)

  // Scrolling into a planet opens it as well.
  await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Störungen melden' })
  await expect(canvas).toHaveAttribute('data-view', 'idle')
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  for (let i = 0; i < 8; i++) await page.mouse.wheel(0, -300)
  await expect(popup.getByRole('heading', { name: 'Störungen melden' })).toBeVisible()
})

test('the galaxy works with the keyboard and as a list', async ({ page }) => {
  const canvas = await openGalaxy(page)

  await canvas.focus()
  await page.keyboard.press('+')
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('0')
  await expect(canvas).toBeFocused()

  await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Projekt-Kickoff durchführen' })
  await expect(page.getByRole('region', { name: 'Projekt-Kickoff durchführen' })).toBeVisible()
  await canvas.focus()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('region', { name: 'Auswahl' })).toBeVisible()

  await page.getByRole('navigation', { name: 'Darstellung' }).getByRole('button', { name: 'Liste' }).click()
  await expect(page.getByRole('heading', { name: /^Anleitung \(\d+\)$/ })).toBeVisible()
  await page.getByRole('button', { name: 'Projekt-Kickoff durchführen', exact: true }).focus()
  await page.keyboard.press('Enter')
  const details = page.getByRole('region', { name: 'Projekt-Kickoff durchführen' })
  await expect(details.getByText('Verweist auf')).toBeVisible()
  await details.getByRole('button', { name: 'Rollen im Projekt' }).click()
  await expect(page.getByRole('region', { name: 'Rollen im Projekt' })).toBeVisible()
})

test('restricted articles do not appear as stars for people without access', async ({ page }) => {
  await openGalaxy(page)
  const options = await page.getByLabel('Artikel fokussieren').locator('option').allTextContents()
  expect(options).toContain('Deployment-Prozess')
  expect(options).not.toContain('Preisliste Vertrieb 2027')
  expect(options).not.toContain('Styleguide Intranet')
})

test.describe('with reduced motion', () => {
  test.use({ contextOptions: { reducedMotion: 'reduce' } })

  test('selecting an article still focuses it, without animation', async ({ page }) => {
    await openGalaxy(page)
    await page.getByLabel('Artikel fokussieren').selectOption({ label: 'Rollen im Projekt' })
    await expect(page.getByRole('region', { name: 'Rollen im Projekt' })).toBeVisible()
    await expect.poll(() => paintedPixels(page)).toBeGreaterThan(1000)
  })
})
