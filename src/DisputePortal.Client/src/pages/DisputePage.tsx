import { useState, type ReactNode } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { useMyDispute, useWithdrawDispute } from '../api/hooks'
import type { DisputeDetail } from '../api/types'
import { Money, StatusBadge, Timeline } from '../components/Display'
import { ErrorAlert, Loading } from '../components/Feedback'
import { formatDateTime, reasonLabels } from '../lib/format'

/** Read-only summary shared by the customer and agent dispute pages. */
export function DisputeOverview({ dispute: d, audience }: { dispute: DisputeDetail; audience: 'customer' | 'agent' }) {
  return (
    <div className="row g-4">
      <div className="col-lg-7">
        <div className="card shadow-sm h-100">
          <div className="card-body">
            <div className="d-flex justify-content-between align-items-start mb-3">
              <h1 className="h4 mb-0">Dispute {d.reference}</h1>
              <StatusBadge status={d.status} audience={audience} />
            </div>
            <dl className="row small mb-0">
              {audience === 'agent' && (
                <>
                  <dt className="col-4">Customer</dt>
                  <dd className="col-8">{d.customerName}</dd>
                </>
              )}
              <dt className="col-4">Reason</dt>
              <dd className="col-8">{reasonLabels[d.reason]}</dd>
              <dt className="col-4">Raised</dt>
              <dd className="col-8">{formatDateTime(d.createdAtUtc)}</dd>
              {d.closedAtUtc && (
                <>
                  <dt className="col-4">Closed</dt>
                  <dd className="col-8">{formatDateTime(d.closedAtUtc)}</dd>
                </>
              )}
              <dt className="col-4">Details</dt>
              <dd className="col-8 text-pre-wrap">{d.details}</dd>
            </dl>
            {d.resolutionNote && (
              <div className={`alert ${d.status === 'Upheld' ? 'alert-success' : 'alert-secondary'} mt-3 mb-0`}>
                <div className="fw-semibold">Outcome</div>
                <p className="mb-0 text-pre-wrap">{d.resolutionNote}</p>
              </div>
            )}
          </div>
        </div>
      </div>
      <div className="col-lg-5">
        <div className="card shadow-sm mb-4">
          <div className="card-body">
            <h2 className="h6">Disputed transaction</h2>
            <div className="d-flex justify-content-between">
              <span>{d.transaction.merchant}</span>
              <Money amount={d.transaction.amount} currency={d.transaction.currency} />
            </div>
            <div className="small text-muted">
              {formatDateTime(d.transaction.postedAtUtc)} · {d.transaction.reference}
            </div>
          </div>
        </div>
        <div className="card shadow-sm">
          <div className="card-body">
            <h2 className="h6 mb-3">History</h2>
            <Timeline items={d.timeline} audience={audience} />
          </div>
        </div>
      </div>
    </div>
  )
}

export function DisputePage() {
  const { id = '' } = useParams()
  const justOpened = (useLocation().state as { justOpened?: boolean } | null)?.justOpened ?? false
  const { data: dispute, error, isPending, refetch } = useMyDispute(id)

  return (
    <>
      <nav aria-label="breadcrumb">
        <ol className="breadcrumb">
          <li className="breadcrumb-item">
            <Link to="/disputes">My disputes</Link>
          </li>
          <li className="breadcrumb-item active" aria-current="page">
            {dispute?.reference ?? 'Dispute'}
          </li>
        </ol>
      </nav>
      {isPending ? (
        <Loading />
      ) : error ? (
        <ErrorAlert error={error} onRetry={() => void refetch()} />
      ) : (
        <>
          {justOpened && (
            <Notice>
              Thanks, we've received your dispute. Your reference is <strong>{dispute.reference}</strong>.
            </Notice>
          )}
          <DisputeOverview dispute={dispute} audience="customer" />
          {dispute.isOpen && <WithdrawPanel dispute={dispute} />}
        </>
      )}
    </>
  )
}

function Notice({ children }: { children: ReactNode }) {
  return (
    <div className="alert alert-success" role="status">
      {children}
    </div>
  )
}

function WithdrawPanel({ dispute }: { dispute: DisputeDetail }) {
  const withdraw = useWithdrawDispute(dispute.id)
  const [confirming, setConfirming] = useState(false)

  return (
    <div className="card shadow-sm mt-4">
      <div className="card-body">
        <h2 className="h6">Sorted it out with the merchant?</h2>
        {withdraw.error && <ErrorAlert error={withdraw.error} />}
        {confirming ? (
          <div className="d-flex flex-wrap align-items-center gap-2">
            <span>Withdraw this dispute? You can raise a new one later if you need to.</span>
            <button type="button" className="btn btn-danger btn-sm" disabled={withdraw.isPending} onClick={() => withdraw.mutate()}>
              {withdraw.isPending ? 'Withdrawing…' : 'Yes, withdraw it'}
            </button>
            <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setConfirming(false)}>
              Keep it open
            </button>
          </div>
        ) : (
          <button type="button" className="btn btn-outline-danger btn-sm" onClick={() => setConfirming(true)}>
            Withdraw dispute
          </button>
        )}
      </div>
    </div>
  )
}
