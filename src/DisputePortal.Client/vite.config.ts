import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The ASP.NET Core host serves the app from its root, on the same origin as the API, so the API needs no
// CORS and the host's Content-Security-Policy ('self' only) covers it. In development the Vite server
// proxies /api to the back end.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../DisputePortal.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': process.env.API_URL ?? 'http://localhost:8080',
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
  },
})
