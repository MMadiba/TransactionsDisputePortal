import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useTransactions } from '../api/hooks'
import { Money, Pager, StatusBadge } from '../components/Display'
import { EmptyState, ErrorAlert, Loading } from '../components/Feedback'
import { formatDate } from '../lib/format'

interface Filters {
  search: string
  from: string
  to: string
  page: number
}

export function TransactionsPage() {
  // Filters live in the URL, so they survive a reload and the back button works.
  const [params, setParams] = useSearchParams()
  const filters: Filters = {
    search: params.get('search') ?? '',
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    page: Number(params.get('page') ?? '1') || 1,
  }
  const [draft, setDraft] = useState(filters)
  const { data, error, isPending, isFetching, refetch } = useTransactions(filters)

  const apply = (next: Filters) => {
    const search = new URLSearchParams()
    if (next.search) search.set('search', next.search)
    if (next.from) search.set('from', next.from)
    if (next.to) search.set('to', next.to)
    if (next.page > 1) search.set('page', String(next.page))
    setParams(search)
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    apply({ ...draft, page: 1 })
  }

  const clear = () => {
    const cleared = { search: '', from: '', to: '', page: 1 }
    setDraft(cleared)
    apply(cleared)
  }

  const isFiltered = !!(filters.search || filters.from || filters.to)

  return (
    <>
      <h1 className="h3 mb-3">Your transactions</h1>

      <form className="card card-body shadow-sm mb-3" onSubmit={onSubmit} role="search" aria-label="Filter transactions">
        <div className="row g-2 align-items-end">
          <div className="col-md-5">
            <label htmlFor="search" className="form-label small">
              Merchant or description
            </label>
            <input id="search" className="form-control" value={draft.search} onChange={(e) => setDraft({ ...draft, search: e.target.value })} />
          </div>
          <div className="col-6 col-md-2">
            <label htmlFor="from" className="form-label small">
              From
            </label>
            <input id="from" type="date" className="form-control" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
          </div>
          <div className="col-6 col-md-2">
            <label htmlFor="to" className="form-label small">
              To
            </label>
            <input id="to" type="date" className="form-control" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
          </div>
          <div className="col-md-3 d-flex gap-2">
            <button type="submit" className="btn btn-primary flex-fill">
              Search
            </button>
            {isFiltered && (
              <button type="button" className="btn btn-outline-secondary" onClick={clear}>
                Clear
              </button>
            )}
          </div>
        </div>
      </form>

      {isPending ? (
        <Loading label="Loading your transactions…" />
      ) : error ? (
        <ErrorAlert error={error} onRetry={() => void refetch()} />
      ) : data.items.length === 0 ? (
        <EmptyState>{isFiltered ? 'No transactions match those filters.' : 'You have no transactions yet.'}</EmptyState>
      ) : (
        <>
          <div className={`card shadow-sm ${isFetching ? 'opacity-75' : ''}`} aria-busy={isFetching}>
            <div className="table-responsive">
              <table className="table table-hover mb-0">
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Merchant</th>
                    <th scope="col" className="text-end">
                      Amount
                    </th>
                    <th scope="col">Dispute</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((t) => (
                    <tr key={t.id}>
                      <td className="text-nowrap">{formatDate(t.postedAtUtc)}</td>
                      <td>
                        <Link to={`/transactions/${t.id}`}>{t.merchant}</Link>
                        <div className="small text-muted">
                          {t.description}
                          {t.status === 'Pending' && <span className="badge text-bg-light border ms-2">Pending</span>}
                        </div>
                      </td>
                      <td className="text-end">
                        <Money amount={t.amount} currency={t.currency} />
                      </td>
                      <td>{t.latestDispute && <StatusBadge status={t.latestDispute.status} />}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
          <Pager result={data} onPage={(page) => apply({ ...filters, page })} />
        </>
      )}
    </>
  )
}
