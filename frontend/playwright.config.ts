import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.PLAYWRIGHT_PORT ?? 4173)
const baseURL = `http://localhost:${port}`

// Playwright project (T054, issue #54) automating quickstart.md Scenario 1 (Discover). Sign-in
// (RequireAuth.tsx) is mocked via a `/api/session` route interception rather than a real Entra ID
// flow — see tests/e2e/scenario1-discover.spec.ts. Backend calls are similarly mocked via
// Playwright route interception so the suite runs without a live API/SQL backend.
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  workers: 2,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'list',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    // Reuses the same specs at a narrow viewport as a lightweight responsive-layout check (T055).
    { name: 'mobile-chromium', use: { ...devices['Pixel 7'] } },
  ],
  webServer: {
    env: {
      VITE_PUBLIC_DEMO_ENABLED: 'false',
      VITE_ENTRA_CLIENT_ID: '11111111-1111-4111-8111-111111111111',
      VITE_ENTRA_API_CLIENT_ID: '22222222-2222-4222-8222-222222222222',
    },
    command: `npm run build && npm run preview -- --port ${port} --strictPort`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
