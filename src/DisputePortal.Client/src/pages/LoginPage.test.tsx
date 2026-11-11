import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { paged, problem, server, tokensFor } from '../test/server'

describe('sign in', () => {
  it('checks the form before calling the server', async () => {
    let calls = 0
    server.use(http.post('/api/auth/token', () => (calls++, HttpResponse.json(tokensFor('customer')))))
    const { user, screen } = renderApp('/login')

    await user.type(await screen.findByLabelText('Email'), 'not-an-email')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it("shows the server's message when the password is wrong", async () => {
    server.use(http.post('/api/auth/token', () => problem(401, 'The email or password is incorrect.')))
    const { user, screen } = renderApp('/login')

    await user.type(await screen.findByLabelText('Email'), 'sipho.dlamini@mmbank.co.za')
    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The email or password is incorrect.')
  })

  it('takes customers to their transactions and agents to the queue', async () => {
    server.use(
      http.post('/api/auth/token', async ({ request }) => {
        const { email, useCookie } = (await request.json()) as { email: string; useCookie: boolean }
        expect(useCookie).toBe(true) // the browser asks for the refresh token as an HttpOnly cookie
        return HttpResponse.json(tokensFor(email.startsWith('naledi') ? 'agent' : 'customer'))
      }),
      http.get('/api/agent/disputes', () => HttpResponse.json(paged([]))),
    )
    const { user, screen, router } = renderApp('/login')

    await user.type(await screen.findByLabelText('Email'), 'naledi.khumalo@mmbank.co.za')
    await user.type(screen.getByLabelText('Password'), 'Demo#Passw0rd')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Dispute queue' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/agent')
  })

  it('sends signed-out visitors to sign in', async () => {
    const signedOut = renderApp('/transactions')
    expect(await signedOut.screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    signedOut.router.dispose()
  })

  it('picks the session back up from the refresh cookie after a reload', async () => {
    server.use(
      http.post('/api/auth/refresh', () => HttpResponse.json(tokensFor('customer'))),
      http.get('/api/transactions', () => HttpResponse.json(paged([]))),
    )
    localStorage.setItem('dispute-portal.signed-in', '1') // left by the earlier sign-in
    const { screen } = renderApp('/transactions')

    expect(await screen.findByRole('heading', { name: 'Your transactions' })).toBeInTheDocument()
    expect(screen.getByText('Sipho Dlamini')).toBeInTheDocument()
  })
})
