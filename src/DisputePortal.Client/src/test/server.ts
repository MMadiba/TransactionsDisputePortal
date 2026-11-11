import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { AuthTokens, DisputeDetail, PagedResult, TransactionDetail, TransactionSummary } from '../api/types'

// ---------- Fake tokens ----------

const base64Url = (value: object) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')

/** An unsigned JWT with the claims the app reads. Only the server checks signatures. */
export function fakeJwt(claims: { sub: string; name: string; email: string; role: string; jti?: string }) {
  return `${base64Url({ alg: 'HS256', typ: 'JWT' })}.${base64Url(claims)}.signature`
}

export const people = {
  customer: { sub: 'customer-1', name: 'Sipho Dlamini', email: 'sipho.dlamini@mmbank.co.za', role: 'Customer' },
  agent: { sub: 'agent-1', name: 'Naledi Khumalo', email: 'naledi.khumalo@mmbank.co.za', role: 'Agent' },
}

let issued = 0
export function tokensFor(person: keyof typeof people): AuthTokens {
  issued++
  return {
    tokenType: 'Bearer',
    accessToken: fakeJwt({ ...people[person], jti: String(issued) }), // unique per token, like the real jti
    expiresIn: 900,
    refreshToken: null, // browser sign-in: it's in a cookie
    refreshTokenExpiresAtUtc: '2026-11-18T08:00:00Z',
  }
}

// ---------- Fixtures ----------

export function paged<T>(items: T[], page = 1, totalCount = items.length, pageSize = 20): PagedResult<T> {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  return { items, page, pageSize, totalCount, totalPages, hasPrevious: page > 1, hasNext: page < totalPages }
}

export const transaction: TransactionSummary = {
  id: 'tx-1',
  reference: 'TXN20261101000001',
  postedAtUtc: '2026-11-01T10:15:00Z',
  merchant: 'Kauai',
  description: 'Card purchase',
  amount: -89.9,
  currency: 'ZAR',
  status: 'Posted',
  latestDispute: null,
}

export const transactionDetail: TransactionDetail = {
  ...transaction,
  cardLast4: '4242',
  canDispute: true,
  cannotDisputeReason: null,
  disputes: [],
}

export function disputeDetail(overrides: Partial<DisputeDetail> = {}): DisputeDetail {
  return {
    id: 'dsp-1',
    reference: 'DSP-ABCD2345',
    status: 'Submitted',
    reason: 'DuplicateCharge',
    details: 'The same amount came off twice within a minute.',
    resolutionNote: null,
    customerName: 'Sipho Dlamini',
    createdAtUtc: '2026-11-02T08:00:00Z',
    updatedAtUtc: '2026-11-02T08:00:00Z',
    closedAtUtc: null,
    transaction,
    timeline: [{ status: 'Submitted', actorRole: 'Customer', actor: 'You', note: null, occurredAtUtc: '2026-11-02T08:00:00Z' }],
    isOpen: true,
    reviewerId: null,
    reviewerName: null,
    ...overrides,
  }
}

export const problem = (status: number, detail: string, code?: string) =>
  HttpResponse.json({ status, title: 'Error', detail, code }, { status, headers: { 'Content-Type': 'application/problem+json' } })

// Default handlers: just enough for signing in and resuming a session. Each test adds what it needs.
export const server = setupServer(
  http.post('/api/auth/refresh', () => problem(401, 'The refresh token is invalid or expired. Please sign in again.')),
  http.post('/api/auth/revoke', () => new HttpResponse(null, { status: 204 })),
)
