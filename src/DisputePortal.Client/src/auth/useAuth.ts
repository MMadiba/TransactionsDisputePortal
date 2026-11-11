import { use } from 'react'
import { AuthContext, type AuthState } from './context'

export function useAuth(): AuthState {
  const value = use(AuthContext)
  if (!value) {
    throw new Error('useAuth must be used inside <AuthProvider>')
  }
  return value
}
