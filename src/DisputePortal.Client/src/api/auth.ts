import { refreshSession, toApiError } from './client'
import { session } from './session'
import type { AuthTokens } from './types'

/** Exchanges email and password for an access token; the refresh token comes back as an HttpOnly cookie. */
export async function signIn(email: string, password: string): Promise<void> {
  const response = await fetch('/api/auth/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, useCookie: true }),
  })
  if (!response.ok) {
    throw await toApiError(response)
  }

  session.start((await response.json()) as AuthTokens)
}

/** Revokes the refresh token on the server, which also clears its cookie, and forgets the session locally. */
export async function signOut(): Promise<void> {
  session.clear()
  try {
    await fetch('/api/auth/revoke', { method: 'POST' })
  } catch {
    // Offline: the refresh token expires on its own.
  }
}

/** On page load, uses the refresh cookie (if there is one) to pick the session back up. */
export function resumeSession(): Promise<boolean> {
  if (session.accessToken) {
    return Promise.resolve(true)
  }
  return session.mayResume ? refreshSession() : Promise.resolve(false)
}
