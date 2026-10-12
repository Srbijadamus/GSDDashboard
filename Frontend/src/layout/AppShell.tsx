import { useState, useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { CommandPalette } from '../components/CommandPalette'
import { WicChatWidget } from '../components/WicChatWidget'
import { useSidebarCollapse } from './useSidebarCollapse'
import { useAuth } from '../auth/AuthContext'

const FULL_BLEED = new Set(['/wic-attendance'])

// The credit sentence itself lives in i18n (common.credit, EN + DE). These proper
// names are identical in both languages and are split out only so they keep the
// existing bold styling inside the translated sentence.
const CREDIT_NAMES = ['Nebojsa Stojnic', 'Ion Ciuceanu', 'Sankar']
const CREDIT_SPLIT = new RegExp(
  `(${CREDIT_NAMES.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`,
)

function CreditLine() {
  const { t } = useTranslation()
  // The translation already carries the spaces ("Developed by Nebojsa Stojnic — …").
  // The outer div is a flex container, so each child span would become a flex item and
  // the browser collapses the leading/trailing space of every item — which is exactly
  // why the names lost their surrounding spaces ("Developed byNebojsa Stojnic—").
  // One inline wrapper keeps the sentence in a single inline formatting context, and
  // whitespace-pre guarantees the EN and DE strings render exactly as written.
  return (
    <span style={{ whiteSpace: "pre" }}>
      {t('common.credit')
        .split(CREDIT_SPLIT)
        .map((part, i) =>
          CREDIT_NAMES.includes(part) ? (
            <span key={i} className="text-ink-muted font-medium">{part}</span>
          ) : (
            <span key={i}>{part}</span>
          ),
        )}
    </span>
  )
}

export function AppShell() {
  const location = useLocation()
  const [cmdOpen, setCmdOpen] = useState(false)
  const { collapsed, toggle } = useSidebarCollapse()
  const { session } = useAuth()
  // AGENT: command palette and chat widget call staff-only APIs — keep them off.
  const isAgent = session?.role === 'AGENT'
  const isFullBleed = FULL_BLEED.has(location.pathname)

  useEffect(() => {
    if (isAgent) return
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault()
        setCmdOpen(true)
      }
    }
    document.addEventListener('keydown', h)
    return () => document.removeEventListener('keydown', h)
  }, [isAgent])

  return (
    <div className="h-screen w-screen overflow-hidden bg-page text-ink flex">
      <Sidebar collapsed={collapsed} onToggle={toggle} />
      <div className="flex-1 flex flex-col min-w-0">
        <Topbar onOpenCmd={() => setCmdOpen(true)} />
        {/* Fixed attribution line. Text comes from i18n (EN/DE), names stay bold. */}
        <div className="h-7 shrink-0 flex items-center px-6 border-b border-line-subtle bg-sunken text-2xs text-ink-soft tracking-[0.01em] truncate">
          <CreditLine />
        </div>
        <main className="flex-1 overflow-y-auto">
          <div className={isFullBleed ? '' : 'mx-auto max-w-[1600px] px-6 py-6'}>
            <Outlet />
          </div>
        </main>
      </div>
      {!isAgent && <CommandPalette isOpen={cmdOpen} onClose={() => setCmdOpen(false)} />}
      {!isAgent && <WicChatWidget />}
    </div>
  )
}
