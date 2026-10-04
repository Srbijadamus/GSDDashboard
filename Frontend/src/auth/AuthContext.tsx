import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'

export type Role = 'AGENT' | 'TEAM_LEAD' | 'RTM' | 'DEV'

export interface Session {
  kid: string
  name: string
  role: Role
  employeeId: string | null
}

interface AuthContextValue {
  session: Session | null
  /** true while the initial /api/auth/me probe is in flight */
  loading: boolean
  /** Throws Error with the server message (e.g. "Unknown KID") on failure. */
  login: (kid: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/auth/me')
      .then(async res => {
        if (res.ok) setSession(await res.json())
      })
      .catch(() => { /* backend unreachable — stay logged out */ })
      .finally(() => setLoading(false))
  }, [])

  const login = useCallback(async (kid: string) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ kid }),
    })
    if (!res.ok) {
      let msg = `Login failed (${res.status})`
      try { const body = await res.json(); if (body?.error) msg = body.error } catch { /* keep default */ }
      throw new Error(msg)
    }
    setSession(await res.json())
  }, [])

  const logout = useCallback(async () => {
    try { await fetch('/api/auth/logout', { method: 'POST' }) } catch { /* clear locally anyway */ }
    setSession(null)
  }, [])

  return (
    <AuthContext.Provider value={{ session, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
