import { ApiError } from '../api/client'

/** A sentence a customer can act on, for any error a request can throw. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.status === 404 ? "We couldn't find that. It may have been removed, or it isn't yours." : error.message
  }
  return "We couldn't reach the server. Check your connection and try again."
}
