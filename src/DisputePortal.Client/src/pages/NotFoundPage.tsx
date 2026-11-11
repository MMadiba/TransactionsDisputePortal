import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <div className="text-center py-5">
      <h1 className="h3">We couldn't find that page</h1>
      <p className="text-muted">The link may be out of date.</p>
      <Link to="/" className="btn btn-primary">
        Back to the portal
      </Link>
    </div>
  )
}
