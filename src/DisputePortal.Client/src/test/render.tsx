import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { session } from '../api/session'
import { AuthProvider } from '../auth/AuthContext'
import { routes } from '../routes'
import { people, tokensFor } from './server'

/**
 * Renders the real app (real routes, guards, providers) at the given URL, optionally already signed in.
 * Only the network is fake.
 */
export function renderApp(url: string, options: { as?: keyof typeof people } = {}) {
  if (options.as) {
    session.start(tokensFor(options.as))
  }

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  const user = userEvent.setup()

  render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )

  return { user, router, screen }
}
