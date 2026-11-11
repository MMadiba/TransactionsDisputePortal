import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { disputeDetail, paged, problem, server, transaction, transactionDetail } from '../test/server'

describe('transactions', () => {
  it('lists transactions and searches through the URL', async () => {
    const searches: (string | null)[] = []
    server.use(
      http.get('/api/transactions', ({ request }) => {
        searches.push(new URL(request.url).searchParams.get('search'))
        return HttpResponse.json(paged([transaction]))
      }),
    )
    const { user, screen, router } = renderApp('/transactions', { as: 'customer' })

    expect(await screen.findByRole('link', { name: 'Kauai' })).toBeInTheDocument()
    expect(screen.getByText('-ZAR 89.90')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Merchant or description'), 'kauai')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    await screen.findByRole('button', { name: 'Clear' })
    expect(router.state.location.search).toBe('?search=kauai')
    expect(searches).toEqual([null, 'kauai'])
  })

  it('shows a retryable error when the transactions fail to load', async () => {
    let attempts = 0
    server.use(
      http.get('/api/transactions', () => (++attempts === 1 ? problem(500, 'Something went wrong.') : HttpResponse.json(paged([transaction])))),
    )
    const { user, screen } = renderApp('/transactions', { as: 'customer' })

    await user.click(await screen.findByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('link', { name: 'Kauai' })).toBeInTheDocument()
  })

  it("explains why a transaction can't be disputed instead of showing the form", async () => {
    server.use(
      http.get('/api/transactions/tx-1', () =>
        HttpResponse.json({ ...transactionDetail, canDispute: false, cannotDisputeReason: "Pending transactions can't be disputed yet." }),
      ),
    )
    const { screen } = renderApp('/transactions/tx-1', { as: 'customer' })

    expect(await screen.findByText("Pending transactions can't be disputed yet.")).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit dispute' })).not.toBeInTheDocument()
  })
})

describe('raising a dispute', () => {
  it('validates the form, submits it and shows the new dispute', async () => {
    let submitted: unknown
    server.use(
      http.get('/api/transactions/tx-1', () => HttpResponse.json(transactionDetail)),
      http.post('/api/transactions/tx-1/disputes', async ({ request }) => {
        submitted = await request.json()
        return HttpResponse.json(disputeDetail(), { status: 201 })
      }),
      http.get('/api/disputes/dsp-1', () => HttpResponse.json(disputeDetail())),
    )
    const { user, screen, router } = renderApp('/transactions/tx-1', { as: 'customer' })

    await user.click(await screen.findByRole('button', { name: 'Submit dispute' }))
    expect(await screen.findByText('Tell us what went wrong.')).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('What went wrong?'), 'DuplicateCharge')
    await user.type(screen.getByLabelText('Tell us more'), 'Charged twice')
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }))
    expect(await screen.findByText(/at least 20 characters/)).toBeInTheDocument()

    await user.type(screen.getByLabelText('Tell us more'), ' within a single minute.')
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }))

    expect(await screen.findByText(/we've received your dispute/)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/disputes/dsp-1')
    expect(submitted).toEqual({ reason: 'DuplicateCharge', details: 'Charged twice within a single minute.' })
  })

  it("shows the server's reason when the dispute is refused", async () => {
    server.use(
      http.get('/api/transactions/tx-1', () => HttpResponse.json(transactionDetail)),
      http.post('/api/transactions/tx-1/disputes', () =>
        problem(409, 'This transaction already has a dispute (DSP-ZZZZ2222).', 'dispute.already_exists'),
      ),
    )
    const { user, screen } = renderApp('/transactions/tx-1', { as: 'customer' })

    await user.selectOptions(await screen.findByLabelText('What went wrong?'), 'DuplicateCharge')
    await user.type(screen.getByLabelText('Tell us more'), 'Charged twice within a single minute.')
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This transaction already has a dispute (DSP-ZZZZ2222).')
  })
})

describe('my dispute', () => {
  it('can be withdrawn after confirming', async () => {
    let withdrawn = false
    server.use(
      http.get('/api/disputes/dsp-1', () => HttpResponse.json(disputeDetail())),
      http.post('/api/disputes/dsp-1/withdraw', () => {
        withdrawn = true
        return HttpResponse.json(disputeDetail({ status: 'Withdrawn', isOpen: false }))
      }),
      http.get('/api/disputes', () => HttpResponse.json(paged([]))),
      http.get('/api/transactions', () => HttpResponse.json(paged([]))),
    )
    const { user, screen } = renderApp('/disputes/dsp-1', { as: 'customer' })

    await user.click(await screen.findByRole('button', { name: 'Withdraw dispute' }))
    expect(withdrawn).toBe(false)
    await user.click(screen.getByRole('button', { name: 'Yes, withdraw it' }))

    expect(await screen.findByText('Withdrawn')).toBeInTheDocument()
    expect(withdrawn).toBe(true)
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument()
  })
})
