import { Link, useSearchParams } from 'react-router'
import { useMyDisputes, useQueue } from '../api/hooks'
import type { DisputeFilter, DisputeSummary } from '../api/types'
import { Money, Pager, StatusBadge } from '../components/Display'
import { EmptyState, ErrorAlert, Loading } from '../components/Feedback'
import { formatDate, reasonLabels } from '../lib/format'

const filters: DisputeFilter[] = ['All', 'Open', 'Closed']

function useFilterParams(defaultFilter: DisputeFilter) {
  const [params, setParams] = useSearchParams()
  const raw = params.get('filter')
  const filter = filters.includes(raw as DisputeFilter) ? (raw as DisputeFilter) : defaultFilter
  const page = Number(params.get('page') ?? '1') || 1
  const go = (next: { filter?: DisputeFilter; page?: number }) =>
    setParams({ filter: next.filter ?? filter, ...(next.page && next.page > 1 ? { page: String(next.page) } : {}) })
  return { filter, page, go }
}

function FilterTabs({ value, onChange }: { value: DisputeFilter; onChange: (filter: DisputeFilter) => void }) {
  return (
    <div className="btn-group mb-3" role="group" aria-label="Show disputes">
      {filters.map((f) => (
        <button
          key={f}
          type="button"
          className={`btn btn-sm ${f === value ? 'btn-primary' : 'btn-outline-primary'}`}
          aria-pressed={f === value}
          onClick={() => onChange(f)}
        >
          {f}
        </button>
      ))}
    </div>
  )
}

function DisputeTable({ items, audience }: { items: DisputeSummary[]; audience: 'customer' | 'agent' }) {
  const base = audience === 'agent' ? '/agent/disputes' : '/disputes'
  return (
    <div className="card shadow-sm">
      <div className="table-responsive">
        <table className="table table-hover mb-0">
          <thead>
            <tr>
              <th scope="col">Reference</th>
              <th scope="col">Raised</th>
              {audience === 'agent' && <th scope="col">Customer</th>}
              <th scope="col">Transaction</th>
              <th scope="col">Reason</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {items.map((d) => (
              <tr key={d.id}>
                <td>
                  <Link to={`${base}/${d.id}`}>{d.reference}</Link>
                </td>
                <td className="text-nowrap">{formatDate(d.createdAtUtc)}</td>
                {audience === 'agent' && <td>{d.customerName}</td>}
                <td>
                  {d.merchant} · <Money amount={d.amount} currency={d.currency} />
                </td>
                <td className="small">{reasonLabels[d.reason]}</td>
                <td>
                  <StatusBadge status={d.status} audience={audience} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export function DisputesPage() {
  const { filter, page, go } = useFilterParams('All')
  const { data, error, isPending, refetch } = useMyDisputes(filter, page)

  return (
    <>
      <h1 className="h3 mb-3">My disputes</h1>
      <FilterTabs value={filter} onChange={(f) => go({ filter: f })} />
      {isPending ? (
        <Loading label="Loading your disputes…" />
      ) : error ? (
        <ErrorAlert error={error} onRetry={() => void refetch()} />
      ) : data.items.length === 0 ? (
        <EmptyState>
          {filter === 'All' ? (
            <>
              You haven't disputed anything yet. Find the transaction in <Link to="/transactions">your transactions</Link> to raise one.
            </>
          ) : (
            `You have no ${filter.toLowerCase()} disputes.`
          )}
        </EmptyState>
      ) : (
        <>
          <DisputeTable items={data.items} audience="customer" />
          <Pager result={data} onPage={(p) => go({ page: p })} />
        </>
      )}
    </>
  )
}

export function QueuePage() {
  const { filter, page, go } = useFilterParams('Open')
  const { data, error, isPending, refetch } = useQueue(filter, page)

  return (
    <>
      <h1 className="h3 mb-1">Dispute queue</h1>
      <p className="text-muted">Open disputes are listed oldest first.</p>
      <FilterTabs value={filter} onChange={(f) => go({ filter: f })} />
      {isPending ? (
        <Loading label="Loading the queue…" />
      ) : error ? (
        <ErrorAlert error={error} onRetry={() => void refetch()} />
      ) : data.items.length === 0 ? (
        <EmptyState>{filter === 'Open' ? 'The queue is empty. Nice work.' : 'No disputes to show.'}</EmptyState>
      ) : (
        <>
          <DisputeTable items={data.items} audience="agent" />
          <Pager result={data} onPage={(p) => go({ page: p })} />
        </>
      )}
    </>
  )
}
