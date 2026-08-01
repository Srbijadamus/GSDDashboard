import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { NAV_GROUPS } from './navItems'

export function Sidebar() {
  const { t } = useTranslation()

  return (
    <aside style={{
      width: 200,
      background: 'var(--sidebar)',
      borderRight: '1px solid var(--border)',
      display: 'flex', flexDirection: 'column', flexShrink: 0,
    }}>
      <div style={{
        padding: '14px 16px', borderBottom: '1px solid var(--border)',
        display: 'flex', alignItems: 'center', gap: 8,
      }}>
        <span style={{
          background: 'var(--accent)', color: '#fff',
          fontSize: 10, fontWeight: 600, padding: '3px 7px',
          borderRadius: 4, fontFamily: 'IBM Plex Mono',
        }}>GSD</span>
        <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text)' }}>
          EON GSD Dashboard
        </span>
      </div>

      <nav style={{ flex: 1, padding: '8px 0', overflowY: 'auto' }}>
        {NAV_GROUPS.flatMap(g => g.items).map(({ to, icon: Icon, i18nKey, label }) => {
          const itemLabel = i18nKey ? t(i18nKey) : (label ?? to)
          return (
            <NavLink key={to} to={to} end={to === '/'}
              style={({ isActive }) => ({
                display: 'flex', alignItems: 'center', gap: 8,
                padding: '9px 16px', fontSize: 12, textDecoration: 'none',
                color: isActive ? 'var(--accent)' : 'var(--text2)',
                background: isActive ? 'rgba(59,126,255,.08)' : 'transparent',
                borderLeft: isActive ? '2px solid var(--accent)' : '2px solid transparent',
                transition: 'all .15s',
              })}>
              <Icon size={14} />
              <span>{itemLabel}</span>
            </NavLink>
          )
        })}
      </nav>
    </aside>
  )
}
