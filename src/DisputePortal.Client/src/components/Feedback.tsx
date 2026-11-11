import type { ReactNode } from 'react'
import { errorMessage } from '../lib/errors'

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="d-flex align-items-center gap-2 text-muted py-4" role="status">
      <span className="spinner-border spinner-border-sm" aria-hidden="true" />
      <span>{label}</span>
    </div>
  )
}

export function ErrorAlert({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div className="alert alert-danger d-flex justify-content-between align-items-center gap-3" role="alert">
      <span>{errorMessage(error)}</span>
      {onRetry && (
        <button type="button" className="btn btn-sm btn-outline-danger" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  )
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="text-muted text-center py-5 mb-0">{children}</p>
}
