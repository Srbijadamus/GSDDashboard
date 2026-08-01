import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Search, Globe } from 'lucide-react'
import { ThemeToggle } from '../components/ThemeToggle'

function LangToggle() {
  const { i18n } = useTranslation()
  return (
    <button
      onClick={() => i18n.changeLanguage(i18n.language === 'en' ? 'de' : 'en')}
      style={{
        background: 'var(--card2)', border: '1px solid var(--border)',
        color: 'var(--text2)', padding: '4px 10px', borderRadius: 6,
        fontSize: 11, cursor: 'pointer', fontFamily: 'IBM Plex Mono',
        display: 'flex', alignItems: 'center', gap: 4,
      }}
    >
      <Globe size={12} />
      {i18n.language === 'en' ? 'DE' : 'EN'}
    </button>
  )
}

interface TopbarProps {
  onOpenCmd: () => void
}

export function Topbar({ onOpenCmd }: TopbarProps) {
  const { t } = useTranslation()
  const location = useLocation()
  const navigate = useNavigate()
  const today = new Date().toLocaleDateString('de-DE')
  const isOverview = location.pathname === '/'
  const horizonParam = new URLSearchParams(location.search).get('horizon') ?? '14'

  return (
    <div style={{
      padding: '10px 20px', borderBottom: '1px solid var(--border)',
      background: 'var(--sidebar)', display: 'flex',
      alignItems: 'center', justifyContent: 'space-between',
    }}>
      <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text)' }}>
        EON GSD Dashboard
      </span>

      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <span style={{ fontSize: 11, color: 'var(--text3)', fontFamily: 'IBM Plex Mono' }}>
          {today}
        </span>

        {isOverview && (
          <div style={{ display: 'flex', gap: 2 }}>
            {['7', '14'].map(h => (
              <button key={h} onClick={() => navigate(`/?horizon=${h}`)}
                style={{
                  background: horizonParam === h ? 'var(--accent)' : 'var(--card2)',
                  border: `1px solid ${horizonParam === h ? 'var(--accent)' : 'var(--border)'}`,
                  color: horizonParam === h ? '#fff' : 'var(--text2)',
                  padding: '3px 9px', borderRadius: 5,
                  fontSize: 11, cursor: 'pointer', fontFamily: 'IBM Plex Mono',
                }}
              >{h}{t('nav.days')}</button>
            ))}
          </div>
        )}

        <button onClick={onOpenCmd} title="Ctrl+K"
          style={{
            background: 'var(--card2)', border: '1px solid var(--border)',
            color: 'var(--text3)', padding: '4px 10px', borderRadius: 6,
            fontSize: 11, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 6,
          }}
        >
          <Search size={12} />
          <kbd style={{ fontFamily: 'IBM Plex Mono', fontSize: 10 }}>Ctrl+K</kbd>
        </button>

        <LangToggle />
        <ThemeToggle />
      </div>
    </div>
  )
}
