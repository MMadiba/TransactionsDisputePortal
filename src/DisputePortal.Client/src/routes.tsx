import type { RouteObject } from 'react-router'
import { Home, Layout, RequireRole } from './components/Layout'
import { DisputePage } from './pages/DisputePage'
import { DisputesPage, QueuePage } from './pages/DisputesPage'
import { LoginPage } from './pages/LoginPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { TransactionPage } from './pages/TransactionPage'
import { TransactionsPage } from './pages/TransactionsPage'
import { ReviewPage } from './pages/agent/ReviewPage'

export const routes: RouteObject[] = [
  {
    element: <Layout />,
    children: [
      { index: true, element: <Home /> },
      { path: 'login', element: <LoginPage /> },
      {
        element: <RequireRole role="Customer" />,
        children: [
          { path: 'transactions', element: <TransactionsPage /> },
          { path: 'transactions/:id', element: <TransactionPage /> },
          { path: 'disputes', element: <DisputesPage /> },
          { path: 'disputes/:id', element: <DisputePage /> },
        ],
      },
      {
        element: <RequireRole role="Agent" />,
        children: [
          { path: 'agent', element: <QueuePage /> },
          { path: 'agent/disputes/:id', element: <ReviewPage /> },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]
