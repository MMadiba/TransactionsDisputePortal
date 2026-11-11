import { session } from './session'
import type { AuthTokens } from './types'

/** An error response from the API, carrying the problem-details fields the UI cares about. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  /** Field errors from a validation problem, keyed by field name in camelCase. */
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Not JSON (a proxy error page, say): fall back to the status line.
  }

  const fieldErrors = Object.fromEntries(
    Object.entries(problem.errors ?? {}).map(([key, messages]) => [key.charAt(0).toLowerCase() + key.slice(1), messages]),
  )
  const message =
    problem.detail ?? Object.values(fieldErrors)[0]?.[0] ?? problem.title ?? `Something went wrong (${response.status}).`

  return new ApiError(response.status, message, problem.code, fieldErrors)
}

// One refresh at a time. The server rotates refresh tokens and treats a replayed one as theft, so two
// parallel refreshes with the same cookie would sign the user out. Every caller waits on the same promise.
let refreshing: Promise<boolean> | null = null

/** Gets a new access token using the HttpOnly refresh cookie. False if there's no valid session. */
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      // No body: the browser sends the refresh cookie, and the new one comes back the same way.
      const response = await fetch('/api/auth/refresh', { method: 'POST' })
      if (!response.ok) {
        session.clear()
        return false
      }

      session.start((await response.json()) as AuthTokens)
      return true
    } catch {
      // Network failure: the cookie is still there, so the next attempt can try again.
      return false
    }
  })().finally(() => {
    refreshing = null
  })

  return refreshing
}

async function send(path: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)
  if (session.accessToken) {
    headers.set('Authorization', `Bearer ${session.accessToken}`)
  }

  return fetch(path, { ...init, headers })
}

/**
 * Calls the API with the current access token. If it has expired (401), the session is refreshed once
 * and the request retried; if that fails, the user is signed out and the original error is thrown.
 */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response = await send(path, init)

  if (response.status === 401 && session.user && (await refreshSession())) {
    response = await send(path, init)
  }

  if (response.status === 401) {
    session.clear()
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export function postJson<T>(path: string, body?: unknown): Promise<T> {
  return api<T>(path, {
    method: 'POST',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

/** Builds a query string, leaving out empty values. */
export function query(params: Record<string, string | number | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}
