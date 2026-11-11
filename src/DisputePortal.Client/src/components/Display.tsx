import type { DisputeHistoryItem, DisputeStatus, PagedResult } from '../api/types'
import { formatDateTime, formatMoney, statusBadge, statusLabel } from '../lib/format'

export function StatusBadge({ status, audience = 'customer' }: { status: DisputeStatus; audience?: 'customer' | 'agent' }) {
  return <span className={`badge ${statusBadge[status]}`}>{statusLabel(status, audience)}</span>
}

export function Money({ amount, currency }: { amount: number; currency: string }) {
  return <span className={`text-nowrap ${amount < 0 ? '' : 'text-success'}`}>{formatMoney(amount, currency)}</span>
}

export function Timeline({ items, audience }: { items: DisputeHistoryItem[]; audience: 'customer' | 'agent' }) {
  return (
    <ol className="timeline list-unstyled mb-0">
      {items.map((item) => (
        <li key={`${item.status}-${item.occurredAtUtc}`} className="timeline-item">
          <div className="fw-semibold">{statusLabel(item.status, audience)}</div>
          <div className="small text-muted">
            {formatDateTime(item.occurredAtUtc)} by {item.actor}
          </div>
          {item.note && <p className="mb-0 mt-1">{item.note}</p>}
        </li>
      ))}
    </ol>
  )
}

export function Pager({ result, onPage }: { result: PagedResult<unknown>; onPage: (page: number) => void }) {
  if (result.totalPages <= 1) {
    return null
  }

  return (
    <nav aria-label="Pages" className="d-flex justify-content-between align-items-center mt-3">
      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasPrevious} onClick={() => onPage(result.page - 1)}>
        Previous
      </button>
      <span className="small text-muted">
        Page {result.page} of {result.totalPages}
      </span>
      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasNext} onClick={() => onPage(result.page + 1)}>
        Next
      </button>
    </nav>
  )
}
