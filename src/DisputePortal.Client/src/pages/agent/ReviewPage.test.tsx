import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../../test/render'
import { disputeDetail, paged, server } from '../../test/server'

const underReviewBy = (reviewerId: string, reviewerName: string) =>
  disputeDetail({ status: 'UnderReview', reviewerId, reviewerName, customerName: 'Sipho Dlamini' })

describe('reviewing a dispute', () => {
  it('offers only "Start review" on a new dispute, then the close form once it is yours', async () => {
    server.use(
      http.get('/api/agent/disputes/dsp-1', () => HttpResponse.json(disputeDetail())),
      http.post('/api/agent/disputes/dsp-1/start-review', () => HttpResponse.json(underReviewBy('agent-1', 'Naledi Khumalo'))),
      http.get('/api/agent/disputes', () => HttpResponse.json(paged([]))),
    )
    const { user, screen } = renderApp('/agent/disputes/dsp-1', { as: 'agent' })

    expect(await screen.findByText('Sipho Dlamini')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close dispute' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Start review' }))

    expect(await screen.findByText("You're reviewing this dispute.")).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close dispute' })).toBeInTheDocument()
  })

  it("doesn't let an agent close a colleague's dispute", async () => {
    server.use(http.get('/api/agent/disputes/dsp-1', () => HttpResponse.json(underReviewBy('agent-2', 'Bo Backup'))))
    const { screen } = renderApp('/agent/disputes/dsp-1', { as: 'agent' })

    expect(await screen.findByText('Bo Backup is reviewing this dispute. Only they can close it.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close dispute' })).not.toBeInTheDocument()
  })

  it('requires an outcome and a note, then closes the dispute', async () => {
    let body: unknown
    server.use(
      http.get('/api/agent/disputes/dsp-1', () => HttpResponse.json(underReviewBy('agent-1', 'Naledi Khumalo'))),
      http.post('/api/agent/disputes/dsp-1/resolve', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(
          disputeDetail({ status: 'Upheld', isOpen: false, resolutionNote: 'Refund issued to your card.', closedAtUtc: '2026-11-03T09:00:00Z' }),
        )
      }),
      http.get('/api/agent/disputes', () => HttpResponse.json(paged([]))),
    )
    const { user, screen } = renderApp('/agent/disputes/dsp-1', { as: 'agent' })

    await user.click(await screen.findByRole('button', { name: 'Close dispute' }))
    expect(await screen.findByText('Choose an outcome.')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Uphold — refund the customer'))
    await user.type(screen.getByLabelText('Note to the customer'), 'Refund issued to your card.')
    await user.click(screen.getByRole('button', { name: 'Close dispute' }))

    expect(await screen.findByText('Refund issued to your card.')).toBeInTheDocument()
    expect(screen.getAllByText('Upheld').length).toBeGreaterThan(0)
    expect(body).toEqual({ outcome: 'Upheld', note: 'Refund issued to your card.' })
  })

  it('keeps customers out of the back office', async () => {
    server.use(http.get('/api/transactions', () => HttpResponse.json(paged([]))))
    const { screen, router } = renderApp('/agent', { as: 'customer' })

    expect(await screen.findByRole('heading', { name: 'Your transactions' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/transactions')
  })
})
