import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { z } from 'zod'
import { ApiError } from '../api/client'
import { useOpenDispute, useTransaction } from '../api/hooks'
import { disputeReasons, limits, type TransactionDetail } from '../api/types'
import { Money, StatusBadge } from '../components/Display'
import { ErrorAlert, Loading } from '../components/Feedback'
import { errorMessage } from '../lib/errors'
import { formatDate, formatDateTime, reasonLabels } from '../lib/format'

export function TransactionPage() {
  const { id = '' } = useParams()
  const { data: transaction, error, isPending, refetch } = useTransaction(id)

  return (
    <>
      <nav aria-label="breadcrumb">
        <ol className="breadcrumb">
          <li className="breadcrumb-item">
            <Link to="/transactions">Transactions</Link>
          </li>
          <li className="breadcrumb-item active" aria-current="page">
            {transaction?.merchant ?? 'Transaction'}
          </li>
        </ol>
      </nav>

      {isPending ? (
        <Loading />
      ) : error ? (
        <ErrorAlert error={error} onRetry={() => void refetch()} />
      ) : (
        <div className="row g-4">
          <div className="col-lg-5">
            <TransactionCard transaction={transaction} />
          </div>
          <div className="col-lg-7">
            {transaction.canDispute ? (
              <DisputeForm transactionId={transaction.id} />
            ) : (
              <div className="alert alert-info" role="status">
                {transaction.cannotDisputeReason}
              </div>
            )}
          </div>
        </div>
      )}
    </>
  )
}

function TransactionCard({ transaction: t }: { transaction: TransactionDetail }) {
  return (
    <div className="card shadow-sm">
      <div className="card-body">
        <h1 className="h4">{t.merchant}</h1>
        <p className="display-6 mb-3">
          <Money amount={t.amount} currency={t.currency} />
        </p>
        <dl className="row mb-0 small">
          <dt className="col-5">Date</dt>
          <dd className="col-7">{formatDateTime(t.postedAtUtc)}</dd>
          <dt className="col-5">Description</dt>
          <dd className="col-7">{t.description}</dd>
          <dt className="col-5">Card</dt>
          <dd className="col-7">•••• {t.cardLast4}</dd>
          <dt className="col-5">Status</dt>
          <dd className="col-7">{t.status}</dd>
          <dt className="col-5">Reference</dt>
          <dd className="col-7">{t.reference}</dd>
        </dl>

        {t.disputes.length > 0 && (
          <>
            <h2 className="h6 mt-4">Disputes on this transaction</h2>
            <ul className="list-unstyled mb-0">
              {t.disputes.map((d) => (
                <li key={d.id} className="d-flex justify-content-between align-items-center py-1">
                  <Link to={`/disputes/${d.id}`}>{d.reference}</Link>
                  <span className="small text-muted">{formatDate(d.createdAtUtc)}</span>
                  <StatusBadge status={d.status} />
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </div>
  )
}

// The same rules the server applies, so most mistakes are caught before a round trip. The server still
// has the final word: its errors are shown against the same fields.
const disputeSchema = z.object({
  reason: z.enum(disputeReasons, { error: 'Tell us what went wrong.' }),
  details: z
    .string()
    .trim()
    .min(limits.detailsMin, `Please give us at least ${limits.detailsMin} characters so we can investigate.`)
    .max(limits.detailsMax, `Please keep it under ${limits.detailsMax} characters.`),
})
type DisputeForm = z.infer<typeof disputeSchema>

export function DisputeForm({ transactionId }: { transactionId: string }) {
  const navigate = useNavigate()
  const openDispute = useOpenDispute(transactionId)
  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors },
  } = useForm<DisputeForm>({ resolver: zodResolver(disputeSchema), defaultValues: { details: '' } })
  const length = (useWatch({ control, name: 'details' }) ?? '').trim().length

  const onSubmit = handleSubmit(async (values) => {
    try {
      const dispute = await openDispute.mutateAsync(values)
      navigate(`/disputes/${dispute.id}`, { state: { justOpened: true } })
    } catch (error) {
      const fieldErrors = error instanceof ApiError ? error.fieldErrors : {}
      const fields = ['reason', 'details'] as const
      const matched = fields.filter((field) => fieldErrors[field]?.[0])
      matched.forEach((field) => setError(field, { message: fieldErrors[field]![0] }))
      if (matched.length === 0) {
        setError('root', { message: errorMessage(error) })
      }
    }
  })

  return (
    <div className="card shadow-sm">
      <div className="card-body">
        <h2 className="h5">Dispute this transaction</h2>
        <p className="small text-muted">We'll look into it and keep you updated here. Most disputes are decided within 10 working days.</p>

        <form onSubmit={onSubmit} noValidate>
          {errors.root && (
            <div className="alert alert-danger" role="alert">
              {errors.root.message}
            </div>
          )}

          <div className="mb-3">
            <label htmlFor="reason" className="form-label">
              What went wrong?
            </label>
            <select
              id="reason"
              className={`form-select ${errors.reason ? 'is-invalid' : ''}`}
              aria-invalid={!!errors.reason}
              aria-describedby={errors.reason ? 'reason-error' : undefined}
              defaultValue=""
              {...register('reason')}
            >
              <option value="" disabled>
                Choose a reason…
              </option>
              {disputeReasons.map((reason) => (
                <option key={reason} value={reason}>
                  {reasonLabels[reason]}
                </option>
              ))}
            </select>
            {errors.reason && (
              <div id="reason-error" className="invalid-feedback">
                {errors.reason.message}
              </div>
            )}
          </div>

          <div className="mb-3">
            <label htmlFor="details" className="form-label">
              Tell us more
            </label>
            <textarea
              id="details"
              rows={5}
              maxLength={limits.detailsMax}
              className={`form-control ${errors.details ? 'is-invalid' : ''}`}
              aria-invalid={!!errors.details}
              aria-describedby={`details-count${errors.details ? ' details-error' : ''}`}
              {...register('details')}
            />
            {errors.details && (
              <div id="details-error" className="invalid-feedback">
                {errors.details.message}
              </div>
            )}
            <div id="details-count" className="form-text">
              {length} / {limits.detailsMax} characters (at least {limits.detailsMin})
            </div>
          </div>

          <button type="submit" className="btn btn-primary" disabled={openDispute.isPending}>
            {openDispute.isPending ? 'Submitting…' : 'Submit dispute'}
          </button>
        </form>
      </div>
    </div>
  )
}
