import type { DisputeReason, DisputeStatus } from '../api/types'

// Dates are shown in the bank's time zone, the same as the server-rendered site, so a customer reading
// the date of a card swipe sees the day it happened locally, wherever their browser is.
const timeZone = 'Africa/Johannesburg'

const dateFormat = new Intl.DateTimeFormat('en-GB', { timeZone, day: '2-digit', month: 'short', year: 'numeric' })
const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone,
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

export const formatDate = (iso: string) => dateFormat.format(new Date(iso))
export const formatDateTime = (iso: string) => dateTimeFormat.format(new Date(iso)).replace(' at ', ', ')

/** "-ZAR 1,250.00": the currency code rather than a symbol, matching statements. */
export function formatMoney(amount: number, currency: string) {
  return new Intl.NumberFormat('en-GB', { style: 'currency', currency, currencyDisplay: 'code' })
    .format(amount)
    .replace(/ /g, ' ')
}

export const reasonLabels: Record<DisputeReason, string> = {
  UnrecognisedTransaction: "I don't recognise this transaction",
  DuplicateCharge: 'I was charged more than once',
  IncorrectAmount: 'The amount is wrong',
  GoodsOrServicesNotReceived: "I didn't receive what I paid for",
  CancelledSubscription: 'I cancelled, but was still charged',
  RefundNotReceived: 'I was promised a refund that never came',
  Other: 'Something else',
}

const customerStatus: Record<DisputeStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  Upheld: 'Resolved in your favour',
  Declined: 'Declined',
  Withdrawn: 'Withdrawn',
}

export function statusLabel(status: DisputeStatus, audience: 'customer' | 'agent' = 'customer') {
  return audience === 'agent' && status === 'Upheld' ? 'Upheld' : customerStatus[status]
}

export const statusBadge: Record<DisputeStatus, string> = {
  Submitted: 'text-bg-primary',
  UnderReview: 'text-bg-warning',
  Upheld: 'text-bg-success',
  Declined: 'text-bg-danger',
  Withdrawn: 'text-bg-secondary',
}
