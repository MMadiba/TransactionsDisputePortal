import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server, tokensFor } from '../test/server'
import { ApiError, api } from './client'
import { readUser, session } from './session'

describe('api client', () => {
  it('refreshes an expired access token once and retries the request', async () => {
    session.start(tokensFor('customer'))
    const stale = session.accessToken
    const fresh = tokensFor('customer')
    let refreshes = 0

    server.use(
      http.get('/api/transactions', ({ request }) =>
        request.headers.get('Authorization') === `Bearer ${stale}`
          ? new HttpResponse(null, { status: 401 })
          : HttpResponse.json({ ok: true }),
      ),
      http.post('/api/auth/refresh', () => {
        refreshes++
        return HttpResponse.json(fresh)
      }),
    )

    await expect(api('/api/transactions')).resolves.toEqual({ ok: true })
    expect(refreshes).toBe(1)
    expect(session.accessToken).toBe(fresh.accessToken)
  })

  it('shares one refresh between parallel requests, because the server treats a reused refresh token as theft', async () => {
    session.start(tokensFor('customer'))
    const stale = session.accessToken
    const fresh = tokensFor('customer')
    let refreshes = 0

    server.use(
      http.get('/api/disputes', ({ request }) =>
        request.headers.get('Authorization') === `Bearer ${stale}` ? new HttpResponse(null, { status: 401 }) : HttpResponse.json([]),
      ),
      http.post('/api/auth/refresh', async ({ request }) => {
        refreshes++
        expect(await request.text()).toBe('') // the refresh token travels in the cookie, never in the body
        return HttpResponse.json(fresh)
      }),
    )

    await Promise.all([api('/api/disputes'), api('/api/disputes'), api('/api/disputes')])

    expect(refreshes).toBe(1)
  })

  it('signs the user out when the session can no longer be refreshed', async () => {
    session.start(tokensFor('customer'))
    server.use(http.get('/api/transactions', () => new HttpResponse(null, { status: 401 })))

    await expect(api('/api/transactions')).rejects.toBeInstanceOf(ApiError)
    expect(session.user).toBeNull()
    expect(session.accessToken).toBeNull()
  })

  it('turns problem details into an ApiError with the stable code and field errors', async () => {
    server.use(
      http.post('/api/things', () =>
        HttpResponse.json(
          { status: 400, title: 'Validation failed', errors: { Details: ['Please give us at least 20 characters.'] } },
          { status: 400 },
        ),
      ),
      http.post('/api/conflict', () =>
        HttpResponse.json({ status: 409, detail: 'This transaction already has a dispute.', code: 'dispute.already_exists' }, { status: 409 }),
      ),
    )

    const validation = (await api('/api/things', { method: 'POST' }).catch((e: unknown) => e)) as ApiError
    expect(validation.fieldErrors.details).toEqual(['Please give us at least 20 characters.'])

    const conflict = (await api('/api/conflict', { method: 'POST' }).catch((e: unknown) => e)) as ApiError
    expect([conflict.status, conflict.code, conflict.message]).toEqual([409, 'dispute.already_exists', 'This transaction already has a dispute.'])
  })
})

describe('readUser', () => {
  it('reads the name, id and roles from the access token', () => {
    const user = readUser(tokensFor('agent').accessToken)

    expect(user).toEqual({ id: 'agent-1', name: 'Naledi Khumalo', email: 'naledi.khumalo@mmbank.co.za', roles: ['Agent'] })
  })
})
