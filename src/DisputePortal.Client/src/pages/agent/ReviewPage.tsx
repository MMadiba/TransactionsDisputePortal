import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router'
import { z } from 'zod'
import { ApiError } from '../../api/client'
import { useResolveDispute, useReviewDispute, useStartReview } from '../../api/hooks'
import { limits, type DisputeDetail } from '../../api/types'
import { useAuth } from '../../auth/useAuth'
import { ErrorAlert, Loading } from '../../components/Feedback'
import { errorMessage } from '../../lib/errors'
import { DisputeOverview } from '../DisputePage'

export function ReviewPage() {
  const { id = '' } = useParams()
  const { data: dispute, error, isPending, refetch } = useReviewDispute(id)

  return (
    <>
      <nav aria-label="breadcrumb">
        <ol className="breadcrumb">
          <li className="breadcrumb-item">
            <Link to="/agent">Dispute queue</Link>
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
          <DisputeOverview dispute={dispute} audience="agent" />
          {dispute.isOpen && <Actions dispute={dispute} />}
        </>
      )}
    </>
  )
}

/** What this agent can do next: pick it up, close it, or nothing if a colleague has it. */
export function Actions({ dispute }: { dispute: DisputeDetail }) {
  const { user } = useAuth()
  const startReview = useStartReview(dispute.id)

  return (
    <div className="card shadow-sm mt-4">
      <div className="card-body">
        <h2 className="h5">Actions</h2>
        {dispute.status === 'Submitted' ? (
          <>
            {startReview.error && <ErrorAlert error={startReview.error} />}
            <button type="button" className="btn btn-primary" disabled={startReview.isPending} onClick={() => startReview.mutate()}>
              {startReview.isPending ? 'Starting…' : 'Start review'}
            </button>
            <span className="small text-muted ms-2">Assigns the dispute to you. Only you can close it once you've picked it up.</span>
          </>
        ) : dispute.reviewerId !== user?.id ? (
          <p className="mb-0">{dispute.reviewerName} is reviewing this dispute. Only they can close it.</p>
        ) : (
          <>
            <p className="small text-muted">You're reviewing this dispute.</p>
            <ResolveForm disputeId={dispute.id} />
          </>
        )}
      </div>
    </div>
  )
}

const resolveSchema = z.object({
  outcome: z.enum(['Upheld', 'Declined'], { error: 'Choose an outcome.' }),
  note: z
    .string()
    .trim()
    .min(limits.noteMin, `The note must be at least ${limits.noteMin} characters.`)
    .max(limits.noteMax, `The note must be under ${limits.noteMax} characters.`),
})
type ResolveForm = z.infer<typeof resolveSchema>

function ResolveForm({ disputeId }: { disputeId: string }) {
  const resolve = useResolveDispute(disputeId)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ResolveForm>({ resolver: zodResolver(resolveSchema), defaultValues: { note: '' } })

  const onSubmit = handleSubmit(async (values) => {
    try {
      await resolve.mutateAsync(values)
    } catch (error) {
      const message = error instanceof ApiError ? error.fieldErrors.note?.[0] : undefined
      setError(message ? 'note' : 'root', { message: message ?? errorMessage(error) })
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      {errors.root && (
        <div className="alert alert-danger" role="alert">
          {errors.root.message}
        </div>
      )}
      <fieldset className="mb-3">
        <legend className="form-label fs-6">Outcome</legend>
        <div className="form-check">
          <input id="upheld" type="radio" value="Upheld" className={`form-check-input ${errors.outcome ? 'is-invalid' : ''}`} {...register('outcome')} />
          <label htmlFor="upheld" className="form-check-label">
            Uphold — refund the customer
          </label>
        </div>
        <div className="form-check">
          <input id="declined" type="radio" value="Declined" className={`form-check-input ${errors.outcome ? 'is-invalid' : ''}`} {...register('outcome')} />
          <label htmlFor="declined" className="form-check-label">
            Decline — the charge stands
          </label>
        </div>
        {errors.outcome && (
          <div className="invalid-feedback d-block" role="alert">
            {errors.outcome.message}
          </div>
        )}
      </fieldset>
      <div className="mb-3">
        <label htmlFor="note" className="form-label">
          Note to the customer
        </label>
        <textarea
          id="note"
          rows={3}
          maxLength={limits.noteMax}
          placeholder="The customer will see this note exactly as written."
          className={`form-control ${errors.note ? 'is-invalid' : ''}`}
          aria-invalid={!!errors.note}
          aria-describedby={errors.note ? 'note-error' : undefined}
          {...register('note')}
        />
        {errors.note && (
          <div id="note-error" className="invalid-feedback">
            {errors.note.message}
          </div>
        )}
      </div>
      <button type="submit" className="btn btn-primary" disabled={resolve.isPending}>
        {resolve.isPending ? 'Closing…' : 'Close dispute'}
      </button>
    </form>
  )
}
