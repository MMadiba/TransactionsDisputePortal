import { NavLink, Navigate, Outlet, useLocation } from 'react-router'
import type { Role } from '../api/session'
import { useAuth } from '../auth/useAuth'
import { homeFor } from '../auth/roles'
import { Loading } from './Feedback'

export function Layout() {
  const { user, signOut } = useAuth()
  const isAgent = user?.roles.includes('Agent') ?? false

  return (
    <>
      <a className="visually-hidden-focusable" href="#main">
        Skip to content
      </a>
      <header>
        <nav className="navbar navbar-expand navbar-dark bg-portal shadow-sm" aria-label="Main">
          <div className="container">
            <span className="navbar-brand fw-semibold d-none d-sm-inline">Dispute Portal</span>
            {user && (
              <>
                <ul className="navbar-nav me-auto">
                  {isAgent ? (
                    <li className="nav-item">
                      <NavLink className="nav-link text-nowrap" to="/agent">
                        Dispute queue
                      </NavLink>
                    </li>
                  ) : (
                    <>
                      <li className="nav-item">
                        <NavLink className="nav-link text-nowrap" to="/transactions">
                          Transactions
                        </NavLink>
                      </li>
                      <li className="nav-item">
                        <NavLink className="nav-link text-nowrap" to="/disputes">
                          My disputes
                        </NavLink>
                      </li>
                    </>
                  )}
                </ul>
                <span className="navbar-text text-white-50 small me-3 d-none d-md-inline">{user.name}</span>
                <button type="button" className="btn btn-sm btn-outline-light text-nowrap" onClick={() => void signOut()}>
                  Sign out
                </button>
              </>
            )}
          </div>
        </nav>
      </header>
      <main id="main" className="container py-4" tabIndex={-1}>
        <Outlet />
      </main>
    </>
  )
}

/** Only lets through users with the given role; everyone else goes to sign in or to their own home page. */
export function RequireRole({ role }: { role: Role }) {
  const { ready, user } = useAuth()
  const location = useLocation()

  if (!ready) {
    return <Loading label="Signing you in…" />
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  if (!user.roles.includes(role)) {
    return <Navigate to={homeFor(user.roles)} replace />
  }

  return <Outlet />
}

/** "/" sends people to where they work, or to sign in. */
export function Home() {
  const { ready, user } = useAuth()
  if (!ready) {
    return <Loading label="Signing you in…" />
  }
  return <Navigate to={user ? homeFor(user.roles) : '/login'} replace />
}
