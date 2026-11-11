import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
import { session } from '../api/session'
import { useAuth } from '../auth/useAuth'
import { errorMessage } from '../lib/errors'
import { homeFor } from '../auth/roles'

const schema = z.object({
  email: z.string().trim().min(1, 'Enter your email address.').pipe(z.email('Enter a valid email address.')),
  password: z.string().min(1, 'Enter your password.'),
})
type Form = z.infer<typeof schema>

export function LoginPage() {
  const { ready, user, signIn } = useAuth()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<Form>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })

  if (ready && user && !isSubmitting) {
    return <Navigate to={homeFor(user.roles)} replace />
  }

  const onSubmit = handleSubmit(async ({ email, password }) => {
    try {
      await signIn(email, password)
      const roles = session.user?.roles ?? []
      // Only go back to where they were heading if it's somewhere their role can actually see.
      const target = from && from.startsWith(homeFor(roles)) ? from : homeFor(roles)
      navigate(target, { replace: true })
    } catch (error) {
      setError('root', { message: errorMessage(error) })
    }
  })

  return (
    <div className="row justify-content-center">
      <div className="col-md-6 col-lg-5">
        <div className="card shadow-sm">
          <div className="card-body p-4">
            <h1 className="h3 mb-3">Sign in</h1>
            <form onSubmit={onSubmit} noValidate>
              {errors.root && (
                <div className="alert alert-danger" role="alert">
                  {errors.root.message}
                </div>
              )}
              <div className="mb-3">
                <label htmlFor="email" className="form-label">
                  Email
                </label>
                <input
                  id="email"
                  type="email"
                  autoComplete="username"
                  placeholder="name@mmbank.co.za"
                  className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                  aria-invalid={!!errors.email}
                  aria-describedby={errors.email ? 'email-error' : undefined}
                  {...register('email')}
                />
                {errors.email && (
                  <div id="email-error" className="invalid-feedback">
                    {errors.email.message}
                  </div>
                )}
              </div>
              <div className="mb-3">
                <label htmlFor="password" className="form-label">
                  Password
                </label>
                <input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  className={`form-control ${errors.password ? 'is-invalid' : ''}`}
                  aria-invalid={!!errors.password}
                  aria-describedby={errors.password ? 'password-error' : undefined}
                  {...register('password')}
                />
                {errors.password && (
                  <div id="password-error" className="invalid-feedback">
                    {errors.password.message}
                  </div>
                )}
              </div>
              <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
                {isSubmitting ? 'Signing in…' : 'Sign in'}
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  )
}
