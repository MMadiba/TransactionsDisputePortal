// Mirrors the DTOs in DisputePortal.Application/Contracts. Enums travel as strings (JsonStringEnumConverter)
// and dates as ISO-8601 UTC strings.

export type TransactionStatus = 'Pending' | 'Posted'
export type DisputeStatus = 'Submitted' | 'UnderReview' | 'Upheld' | 'Declined' | 'Withdrawn'
export type DisputeOutcome = 'Upheld' | 'Declined'
export type ActorRole = 'Customer' | 'Agent' | 'System'
export type DisputeFilter = 'All' | 'Open' | 'Closed'

export const disputeReasons = [
  'UnrecognisedTransaction',
  'DuplicateCharge',
  'IncorrectAmount',
  'GoodsOrServicesNotReceived',
  'CancelledSubscription',
  'RefundNotReceived',
  'Other',
] as const
export type DisputeReason = (typeof disputeReasons)[number]

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export interface DisputeLink {
  id: string
  reference: string
  status: DisputeStatus
}

export interface TransactionSummary {
  id: string
  reference: string
  postedAtUtc: string
  merchant: string
  description: string
  amount: number
  currency: string
  status: TransactionStatus
  latestDispute: DisputeLink | null
}

export interface TransactionDetail extends Omit<TransactionSummary, 'latestDispute'> {
  cardLast4: string
  canDispute: boolean
  cannotDisputeReason: string | null
  disputes: DisputeSummary[]
}

export interface DisputeSummary {
  id: string
  reference: string
  transactionId: string
  transactionReference: string
  merchant: string
  amount: number
  currency: string
  reason: DisputeReason
  status: DisputeStatus
  customerName: string
  createdAtUtc: string
  updatedAtUtc: string
  closedAtUtc: string | null
}

export interface DisputeHistoryItem {
  status: DisputeStatus
  actorRole: ActorRole
  actor: string
  note: string | null
  occurredAtUtc: string
}

export interface DisputeDetail {
  id: string
  reference: string
  status: DisputeStatus
  reason: DisputeReason
  details: string
  resolutionNote: string | null
  customerName: string
  createdAtUtc: string
  updatedAtUtc: string
  closedAtUtc: string | null
  transaction: TransactionSummary
  timeline: DisputeHistoryItem[]
  isOpen: boolean
  reviewerId: string | null
  reviewerName: string | null
}

export interface AuthTokens {
  tokenType: 'Bearer'
  accessToken: string
  expiresIn: number
  /** Null for browser sign-in: the refresh token is in an HttpOnly cookie the app never sees. */
  refreshToken: string | null
  refreshTokenExpiresAtUtc: string
}

export interface OpenDisputeRequest {
  reason: DisputeReason
  details: string
}

export interface ResolveDisputeRequest {
  outcome: DisputeOutcome
  note: string
}

/** Same limits the server enforces (DisputeLimits in the domain). */
export const limits = {
  detailsMin: 20,
  detailsMax: 2000,
  noteMin: 10,
  noteMax: 1000,
} as const
