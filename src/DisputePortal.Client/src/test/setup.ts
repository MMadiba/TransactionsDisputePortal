import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { session } from '../api/session'
import { server } from './server'

// The app calls the API with relative URLs ("/api/..."), as a browser would. Node's fetch has no page to
// resolve them against, so resolve them against jsdom's location; MSW then matches them as usual.
const nodeFetch = globalThis.fetch
globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) =>
  nodeFetch(typeof input === 'string' && input.startsWith('/') ? new URL(input, window.location.origin) : input, init)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  cleanup()
  server.resetHandlers()
  session.clear()
})
afterAll(() => server.close())
