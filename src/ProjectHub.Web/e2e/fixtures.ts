import { expect, test as base } from '@playwright/test'
import { cspViolations } from './helpers'

/**
 * Every test fails on a Content Security Policy violation. The policy is only sent by the web container
 * (deploy/security-headers.json), so this matters when the tests run against it (PROJECTHUB_E2E_BASE_URL).
 */
export const test = base.extend<{ cspGuard: void }>({
  cspGuard: [
    // Playwright requires a destructuring pattern here, even when the fixture uses no others.
    // oxlint-disable-next-line no-empty-pattern
    async ({}, use) => {
      cspViolations.length = 0
      await use()
      expect(cspViolations, 'Content Security Policy violations').toEqual([])
    },
    { auto: true },
  ],
})

export { expect }
