/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const apiUrl = process.env.PROJECTHUB_API_URL ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // ws: the realtime hub below /api/v1/hubs upgrades to WebSockets.
      '/api': { target: apiUrl, ws: true },
      '/health': apiUrl,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Playwright runs the end-to-end tests in e2e/ (npm run e2e).
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
