import { useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/AuthContext'

export default function Login() {
  const { t } = useTranslation()
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from

  const [kid, setKid] = useState('')
  // Supervisor credentials. Auth:SupervisorMode=enforce refuses the KID-only
  // body, and before this existed there was no way to supply a password or an
  // authenticator code from the UI at all - flipping the flag would have locked
  // every user out (independent review, finding 6).
  const [useCreds, setUseCreds] = useState(false)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      if (useCreds) {
        await login('', { username: username.trim(), password, totp: totp.trim() || undefined })
      } else {
        await login(kid)
      }
      navigate(from && from !== '/login' ? from : '/', { replace: true })
    } catch (err) {
      // Neutral by design — the server never reveals whether a KID exists.
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-h-screen w-full grid place-items-center bg-page text-ink px-4">
      <div className="w-full max-w-sm rounded-xl border border-line-subtle bg-raised shadow-sm p-8">
        <div className="flex items-center gap-2.5 mb-6">
          <svg width="28" height="28" viewBox="0 0 32 32" aria-hidden="true" className="shrink-0">
            <rect width="32" height="32" rx="7" fill="rgb(var(--brand-accent))" />
            <path d="M5 16h5.2l2.6-6.4 4.2 12.8 2.6-6.4H27"
                  fill="none" stroke="#FFFFFF" strokeWidth="2.6"
                  strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <p className="text-sm font-semibold leading-tight">WorkForce Pulse</p>
        </div>

        <h1 className="text-lg font-semibold mb-1">{t('auth.title')}</h1>
        <p className="text-sm text-ink-muted mb-6">{t('auth.subtitle')}</p>

        <form onSubmit={onSubmit} className="space-y-4">
          {useCreds ? (
            <>
              <div>
                <label htmlFor="username" className="block text-xs font-medium text-ink-muted mb-1.5">
                  Username
                </label>
                <input
                  id="username" type="text" autoComplete="username" autoFocus
                  value={username} onChange={e => setUsername(e.target.value)}
                  className="w-full h-9 rounded-md border border-line-default bg-page px-3 text-sm text-ink
                             focus:outline-none focus:border-line-strong"
                  placeholder="e.g. t.rossberg"
                />
              </div>
              <div>
                <label htmlFor="password" className="block text-xs font-medium text-ink-muted mb-1.5">
                  Password
                </label>
                <input
                  id="password" type="password" autoComplete="current-password"
                  value={password} onChange={e => setPassword(e.target.value)}
                  className="w-full h-9 rounded-md border border-line-default bg-page px-3 text-sm text-ink
                             focus:outline-none focus:border-line-strong"
                />
              </div>
              <div>
                <label htmlFor="totp" className="block text-xs font-medium text-ink-muted mb-1.5">
                  Authenticator code
                </label>
                <input
                  id="totp" type="text" inputMode="numeric" maxLength={6} autoComplete="one-time-code"
                  value={totp} onChange={e => setTotp(e.target.value)}
                  className="w-full h-9 rounded-md border border-line-default bg-page px-3 text-sm text-ink
                             focus:outline-none focus:border-line-strong"
                  placeholder="6 digits"
                />
                <p className="mt-1.5 text-2xs text-ink-soft">
                  Team Lead, RTM and Developer only.
                </p>
              </div>
            </>
          ) : (
          <div>
            <label htmlFor="kid" className="block text-xs font-medium text-ink-muted mb-1.5">
              {t('auth.kidLabel')}
            </label>
            <input
              id="kid"
              type="text"
              autoComplete="username"
              autoFocus
              value={kid}
              onChange={e => setKid(e.target.value)}
              className="w-full h-9 rounded-md border border-line-default bg-page px-3 text-sm text-ink
                         focus:outline-none focus:border-line-strong"
              placeholder={t('auth.kidPlaceholder')}
            />
            {/* Helper text per decision: the employee number also works. */}
            <p className="mt-1.5 text-2xs text-ink-soft">{t('auth.kidHelp')}</p>
          </div>
          )}

          <button
            type="button"
            onClick={() => { setUseCreds(v => !v); setError(null) }}
            className="text-2xs text-ink-soft underline underline-offset-2 hover:text-ink-muted"
          >
            {useCreds ? 'Sign in with KID instead' : 'Sign in with an account and authenticator code'}
          </button>

          {error && (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">{error}</p>
          )}

          <button
            type="submit"
            disabled={busy || (useCreds
              ? username.trim().length === 0 || password.length === 0
              : kid.trim().length === 0)}
            className="w-full h-9 rounded-md bg-accent text-white text-sm font-medium
                       hover:opacity-90 disabled:opacity-50 transition-opacity"
          >
            {busy ? t('auth.signingIn') : t('auth.signIn')}
          </button>
        </form>
      </div>
    </div>
  )
}
