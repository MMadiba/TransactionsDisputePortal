import { defineConfig, devices } from '@playwright/test'

// Runs against a real, running portal with demo data, by default the Docker container from the README:
//   docker run ... -e DemoData__Enabled=true -e DemoData__Password='Demo#Passw0rd' ... dispute-portal
// Point it elsewhere with E2E_BASE_URL.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
