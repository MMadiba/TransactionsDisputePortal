import { useCallback, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { resumeSession, signIn, signOut } from '../api/auth'
import { session } from '../api/session'
import { AuthContext, type AuthState } from './context'

export function AuthProvider({ children }: { children: ReactNode }) {
  const user = useSyncExternalStore(session.subscribe, () => session.user)
  const [ready, setReady] = useState(false)
  const queryClient = useQueryClient()

  useEffect(() => {
    let active = true
    void resumeSession().finally(() => active && setReady(true))
    return () => {
      active = false
    }
  }, [])

  // Whoever signs in next must never see the previous user's cached data.
  useEffect(() => session.subscribe(() => !session.user && queryClient.clear()), [queryClient])

  const handleSignOut = useCallback(async () => {
    await signOut()
    queryClient.clear()
  }, [queryClient])

  const value = useMemo<AuthState>(() => ({ ready, user, signIn, signOut: handleSignOut }), [ready, user, handleSignOut])
  return <AuthContext value={value}>{children}</AuthContext>
}
