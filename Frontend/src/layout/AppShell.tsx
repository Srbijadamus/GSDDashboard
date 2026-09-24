import { useState, useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { CommandPalette } from '../components/CommandPalette'
import { WicChatWidget } from '../components/WicChatWidget'
import { useSidebarCollapse } from './useSidebarCollapse'
import { useAuth } from '../auth/AuthContext'

const FULL_BLEED = new Set(['/wic-attendance'])

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
        {/* i18n exception (approved): fixed attribution, identical in EN and DE. */}
        <div className="h-7 shrink-0 flex items-center px-6 border-b border-line-subtle bg-sunken text-2xs text-ink-soft tracking-[0.01em] truncate">
          Developed by <span className="text-ink-muted font-medium mx-1">Nebojsa Stojnic</span>
          — with the selfless support of{' '}
          <span className="text-ink-muted font-medium ml-1">Ion Ciuceanu</span>.
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
