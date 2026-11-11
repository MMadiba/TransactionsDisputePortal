import type { Role } from '../api/session'

/** Each role's landing page. */
export function homeFor(roles: Role[]) {
  return roles.includes('Agent') ? '/agent' : '/transactions'
}
