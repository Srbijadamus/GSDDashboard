import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, Sun, Moon } from 'lucide-react'
import { NAV_GROUPS } from './navItems'
import { useNavTheme } from './useNavTheme'

declare const __APP_VERSION__: string

interface SidebarProps {
  collapsed: boolean
  onToggle: () => void
}

export function Sidebar({ collapsed, onToggle }: SidebarProps) {
  const { t } = useTranslation()
  const { navTheme, toggle: toggleNavTheme } = useNavTheme()

  return (
    <aside className={[
      'shrink-0 h-full bg-nav-surface border-r border-nav-border flex flex-col',
      'transition-[width] duration-base ease-out',
      collapsed ? 'w-16' : 'w-[248px]',
    ].join(' ')}>

      {/* Logo block */}
      <div className="h-14 px-4 flex items-center gap-3 border-b border-nav-border shrink-0">
        <span className="h-7 w-7 rounded-md bg-eon text-eon-fg grid place-items-center text-2xs font-bold shrink-0">
          GSD
        </span>
        {!collapsed && (
          <div className="min-w-0">
            <p className="text-sm font-semibold text-nav-ink leading-tight truncate">
              EON GSD Dashboard
            </p>
            <p className="text-2xs text-nav-soft leading-tight truncate">
              Infosys · E.ON
            </p>
          </div>
        )}
      </div>

      {/* Nav groups */}
      <div className="flex-1 nav-scroll overflow-y-auto py-2 px-2 space-y-3">
        {NAV_GROUPS.map((group, gi) => (
          <nav key={gi}>
            {group.groupKey && (
              collapsed
                ? <div className="h-px bg-nav-border mx-2 my-2" />
                : (
                  <p className="px-3 pb-1.5 text-2xs font-semibold uppercase tracking-[0.06em] text-nav-soft">
                    {t(group.groupKey)}
                  </p>
                )
            )}

            {group.items.map(({ to, icon: Icon, i18nKey }) => {
              const label = i18nKey ? t(i18nKey) : to
              return (
                <NavLink
                  key={to}
                  to={to}
                  end={to === '/'}
                  title={collapsed ? label : undefined}
                  className={({ isActive }) => [
                    'group relative flex items-center gap-3 h-8 rounded-md',
                    'text-sm font-medium transition-colors duration-fast ease-out',
                    collapsed ? 'justify-center px-0' : 'px-3',
                    isActive
                      ? 'bg-nav-active text-nav-ink font-semibold'
                      : 'text-nav-muted hover:bg-nav-hover hover:text-nav-ink',
                  ].join(' ')}
                >
                  {({ isActive }) => (
                    <>
                      {isActive && (
                        <span className="absolute left-0 top-1 bottom-1 w-[3px] rounded-full bg-nav-rail" />
                      )}
                      <Icon className={[
                        'h-4 w-4 shrink-0',
                        isActive ? 'text-nav-ink' : 'text-nav-soft group-hover:text-nav-muted',
                      ].join(' ')} />
                      {!collapsed && <span className="truncate">{label}</span>}
                    </>
                  )}
                </NavLink>
              )
            })}
          </nav>
        ))}
      </div>

      {/* Footer */}
      <div className="shrink-0 border-t border-nav-border p-2">
        <div className={[
          'flex',
          collapsed ? 'flex-col items-center gap-1' : 'items-center gap-1',
        ].join(' ')}>

          <button
            onClick={onToggle}
            aria-label={collapsed ? t('nav.expandSidebar') : t('nav.collapseSidebar')}
            className={[
              'h-9 rounded-md flex items-center transition-colors duration-fast',
              'text-nav-muted hover:bg-nav-hover',
              collapsed ? 'w-9 justify-center' : 'flex-1 gap-3 px-3',
            ].join(' ')}
          >
            <ChevronLeft className={[
              'h-4 w-4 shrink-0 transition-transform duration-base',
              collapsed ? 'rotate-180' : '',
            ].join(' ')} />
            {!collapsed && (
              <span className="truncate text-sm">{t('nav.collapseSidebar')}</span>
            )}
          </button>

          <button
            onClick={toggleNavTheme}
            aria-label={navTheme === 'dark' ? t('nav.navThemeToLight') : t('nav.navThemeToDark')}
            className="h-9 w-9 rounded-md flex items-center justify-center text-nav-muted hover:bg-nav-hover transition-colors duration-fast shrink-0"
          >
            {navTheme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </button>
        </div>

        {!collapsed && (
          <p className="text-2xs text-nav-soft px-3 pt-1">v{__APP_VERSION__} · GSD Ops</p>
        )}
      </div>
    </aside>
  )
}
