import { useState, useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { CommandPalette } from '../components/CommandPalette'
import { WicChatWidget } from '../components/WicChatWidget'

const FULL_BLEED = new Set(['/wic-attendance'])

export function AppShell() {
  const location = useLocation()
  const [cmdOpen, setCmdOpen] = useState(false)
  const isFullBleed = FULL_BLEED.has(location.pathname)

  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault()
        setCmdOpen(true)
      }
    }
    document.addEventListener('keydown', h)
    return () => document.removeEventListener('keydown', h)
  }, [])

  return (
    <div style={{ display: 'flex', height: '100vh', overflow: 'hidden', background: 'var(--bg)' }}>
      <Sidebar />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        <Topbar onOpenCmd={() => setCmdOpen(true)} />
        <main style={{ flex: 1, overflowY: 'auto' }}>
          {isFullBleed
            ? <Outlet />
            : <div style={{ padding: 20 }}><Outlet /></div>}
        </main>
      </div>
      <CommandPalette isOpen={cmdOpen} onClose={() => setCmdOpen(false)} />
      <WicChatWidget />
    </div>
  )
}
