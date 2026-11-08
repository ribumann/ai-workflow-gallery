import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.PLAYWRIGHT_PUBLIC_PORT ?? 4184)
export default defineConfig({
  testDir: './tests/public-e2e',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: { baseURL: `http://localhost:${port}`, trace: 'on-first-retry' },
  projects: [
    { name: 'public-desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'public-mobile', use: { ...devices['Pixel 7'] } },
  ],
  webServer: {
    env: { VITE_PUBLIC_DEMO_ENABLED: 'true', VITE_ENTRA_CLIENT_ID: '', VITE_ENTRA_API_CLIENT_ID: '' },
    command: `npm run build && npm run preview -- --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
