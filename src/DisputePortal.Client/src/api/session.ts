import type { AuthTokens } from './types'

/**
 * The signed-in session, as far as the page can see it.
 *
 * - The access token stays in memory only. It is short-lived (15 minutes) and never written to storage.
 * - The refresh token never reaches the page at all: the server keeps it in an HttpOnly, SameSite=Strict
 *   cookie scoped to /api/auth. A reload or a new tab gets a fresh access token by calling /api/auth/refresh.
 * - A plain flag in localStorage remembers only that a session exists (no token, nothing secret), so a
 *   first-time visitor isn't sent on a refresh call that can only fail.
 */
const hintKey = 'dispute-portal.signed-in'

function setHint(signedIn: boolean) {
  try {
    if (signedIn) localStorage.setItem(hintKey, '1')
    else localStorage.removeItem(hintKey)
  } catch {
    // Storage blocked: resuming just costs one extra request.
  }
}

export type Role = 'Customer' | 'Agent'

export interface SessionUser {
  id: string
  name: string
  email: string
  roles: Role[]
}

let accessToken: string | null = null
let user: SessionUser | null = null
const listeners = new Set<() => void>()

function notify() {
  listeners.forEach((listener) => listener())
}

export const session = {
  get accessToken() {
    return accessToken
  },

  get user() {
    return user
  },

  /** Whether there may be a refresh cookie worth trying after a reload. */
  get mayResume(): boolean {
    try {
      return localStorage.getItem(hintKey) === '1'
    } catch {
      return true
    }
  },

  start(tokens: AuthTokens) {
    accessToken = tokens.accessToken
    user = readUser(tokens.accessToken)
    setHint(true)
    notify()
  },

  clear() {
    accessToken = null
    user = null
    setHint(false)
    notify()
  },

  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
}

/**
 * Reads the display claims from the access token. This is for the UI only: the server validates the
 * signature on every request, so a tampered token gets nothing more than a 401.
 */
export function readUser(token: string): SessionUser {
  const payload = token.split('.')[1]
  if (!payload) {
    throw new Error('Malformed access token')
  }

  const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '='))
  const claims = JSON.parse(new TextDecoder().decode(Uint8Array.from(json, (c) => c.charCodeAt(0)))) as {
    sub: string
    name: string
    email: string
    role?: string | string[]
  }
  const roles = (Array.isArray(claims.role) ? claims.role : claims.role ? [claims.role] : []).filter(
    (r): r is Role => r === 'Customer' || r === 'Agent',
  )

  return { id: claims.sub, name: claims.name, email: claims.email, roles }
}
