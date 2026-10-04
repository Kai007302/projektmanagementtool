import { defineConfig, devices } from '@playwright/test'

// End-to-end tests against the real API and the Vite dev server with the synthetic development data.
// PostgreSQL and Redis must run (docker compose up -d in the repository root); the API applies the
// migrations and seeds the development data itself. Running servers are reused.
// With PROJECTHUB_E2E_BASE_URL (e.g. http://localhost:8080 from `docker compose --profile app up`) the tests run
// against the containers instead, including the web container's Content Security Policy (ADR 0012).
const containerUrl = process.env.PROJECTHUB_E2E_BASE_URL

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: containerUrl ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
    viewport: { width: 1280, height: 900 },
  },
  webServer: containerUrl ? [] : [
    {
      command: 'dotnet run --project ../ProjectHub.Api --launch-profile http',
      url: 'http://localhost:5080/health/ready',
      reuseExistingServer: true,
      timeout: 300_000,
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
})
