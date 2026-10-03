import { defineConfig, devices } from '@playwright/test'

// End-to-end tests against the real API and the Vite dev server with the synthetic development data.
// PostgreSQL and Redis must run (docker compose up -d in the repository root); the API applies the
// migrations and seeds the development data itself. Running servers are reused.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
    viewport: { width: 1280, height: 900 },
  },
  webServer: [
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
