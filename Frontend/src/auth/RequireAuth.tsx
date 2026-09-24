import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from './AuthContext'

/**
 * Gate for the whole app shell: unauthenticated users go to /login.
 * AGENT sessions are confined to /my — every other route redirects there.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { session, loading } = useAuth()
  const location = useLocation()

  if (loading) {
    return (
      <div className="h-screen w-screen grid place-items-center bg-page text-ink-soft text-sm">
        …
      </div>
    )
  }
  if (!session) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }
  if (session.role === 'AGENT' && location.pathname !== '/my') {
    return <Navigate to="/my" replace />
  }
  return <>{children}</>
}
