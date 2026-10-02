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
  login: (kid: string, creds?: { username: string; password: string; totp?: string; recoveryCode?: string }) => Promise<void>
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

  // Two shapes, one endpoint. Which one the server accepts is decided by
  // Auth:SupervisorMode (off | shadow | enforce) in appsettings.json:
  //   off / shadow -> the KID-only body still works, exactly as before
  //   enforce      -> only username + password + authenticator code
  // Sending the credentials when the user filled them in means this build works
  // in BOTH modes, so the frontend can ship before the flag is flipped.
  const login = useCallback(async (
    kid: string,
    creds?: { username: string; password: string; totp?: string; recoveryCode?: string },
  ) => {
    const body: Record<string, string> = creds
      ? {
          username: creds.username,
          password: creds.password,
          ...(creds.totp ? { totp: creds.totp } : {}),
          ...(creds.recoveryCode ? { recoveryCode: creds.recoveryCode } : {}),
        }
      : { kid }
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
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
