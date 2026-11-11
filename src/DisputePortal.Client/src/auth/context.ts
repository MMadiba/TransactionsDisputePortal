import { createContext } from 'react'
import type { SessionUser } from '../api/session'

export interface AuthState {
  /** False until a stored session has been resumed (or found missing), so routes don't flash the login page. */
  ready: boolean
  user: SessionUser | null
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthState | null>(null)
