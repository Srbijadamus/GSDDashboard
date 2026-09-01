import { useLocation, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'
import { ThemeToggle } from '../components/ThemeToggle'
import { NAV_GROUPS } from './navItems'

function LangToggle() {
  const { i18n, t } = useTranslation()
  return (
    <button
      onClick={() => i18n.changeLanguage(i18n.language === 'en' ? 'de' : 'en')}
      aria-label={t('topbar.switchLanguage')}
      className="h-8 w-8 rounded-md flex items-center justify-center text-2xs font-semibold text-ink-muted hover:bg-hovered transition-colors duration-fast"
    >
      {i18n.language === 'en' ? 'DE' : 'EN'}
    </button>
  )
}

interface TopbarProps {
  onOpenCmd: () => void
}

export function Topbar({ onOpenCmd }: TopbarProps) {
  const { t, i18n } = useTranslation()
  const location = useLocation()
  const [params, setParams] = useSearchParams()

  const isOverview = location.pathname === '/'
  const horizonParam = params.get('horizon') ?? '28'

  const setHorizon = (h: string) => {
    const next = new URLSearchParams(params)
    next.set('horizon', h)
    setParams(next, { replace: true })
  }

  const dateLocale = i18n.language === 'de' ? 'de-DE' : 'en-GB'
  const today = new Date().toLocaleDateString(dateLocale, {
    day: '2-digit', month: '2-digit', year: 'numeric',
  })

  // Longest path first so /wic-attendance wins over /wic on prefix match
  const allItems = [...NAV_GROUPS.flatMap(g => g.items)]
    .sort((a, b) => b.to.length - a.to.length)
  const currentItem = allItems.find(item =>
    item.to === '/'
      ? location.pathname === '/'
      : location.pathname === item.to || location.pathname.startsWith(item.to + '/')
  )
  const pageTitle = currentItem?.i18nKey ? t(currentItem.i18nKey) : 'EON GSD Dashboard'

  return (
    <header className="h-14 shrink-0 sticky top-0 z-topbar bg-raised/80 backdrop-blur-md border-b border-line-subtle flex items-center gap-4 px-6">

      <span className="text-md font-semibold text-ink">{pageTitle}</span>

      <div className="ml-auto flex items-center gap-2">

        <time className="text-sm text-ink-muted tnum">{today}</time>

        {isOverview && (
          <div className="h-8 p-0.5 rounded-md bg-sunken border border-line-subtle flex">
            {[
              { h: '7',  ariaLabel: t('topbar.horizon7') },
              { h: '14', ariaLabel: t('topbar.horizon14') },
              { h: '28', ariaLabel: t('topbar.horizon28') },
            ].map(({ h, ariaLabel }) => (
              <button key={h}
                onClick={() => setHorizon(h)}
                aria-label={ariaLabel}
                className={[
                  'h-7 px-3 rounded-[5px] text-xs font-medium transition-colors duration-fast',
                  horizonParam === h
                    ? 'bg-raised text-ink shadow-xs'
                    : 'text-ink-muted hover:text-ink',
                ].join(' ')}
              >
                {h}{t('nav.days')}
              </button>
            ))}
          </div>
        )}

        {/* Search — full trigger on xl+, icon-only below */}
        <button onClick={onOpenCmd}
          aria-label={t('topbar.search')}
          className="h-8 pl-2.5 pr-2 rounded-md border border-line-default bg-raised hidden xl:flex items-center gap-2 text-sm text-ink-soft hover:border-line-strong transition-colors duration-fast w-56"
        >
          <Search className="h-4 w-4 shrink-0" />
          <span className="flex-1 text-left truncate">{t('nav.cmdPlaceholder')}</span>
          <kbd className="ml-auto h-5 px-1.5 rounded-xs bg-sunken border border-line-subtle text-2xs font-medium text-ink-soft grid place-items-center">
            Ctrl K
          </kbd>
        </button>
        <button onClick={onOpenCmd}
          aria-label={t('topbar.search')}
          className="h-8 w-8 rounded-md flex items-center justify-center text-ink-muted hover:bg-hovered xl:hidden transition-colors duration-fast"
        >
          <Search className="h-4 w-4" />
        </button>

        <div className="h-5 w-px bg-line-subtle mx-1" />
        <LangToggle />
        <ThemeToggle />
      </div>
    </header>
  )
}
