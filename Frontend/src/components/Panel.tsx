import { useState, type ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'

interface PanelProps {
  title: string
  collapsible?: boolean
  storageKey?: string
  defaultOpen?: boolean
  children: ReactNode
  className?: string
}

export function Panel({ title, collapsible, storageKey, defaultOpen = true, children, className }: PanelProps) {
  const [open, setOpen] = useState<boolean>(() => {
    if (!collapsible) return true
    if (storageKey) {
      try { const v = localStorage.getItem(storageKey); if (v !== null) return v === 'true' } catch {}
    }
    return defaultOpen
  })

  const toggle = () => {
    const next = !open
    setOpen(next)
    if (storageKey) { try { localStorage.setItem(storageKey, String(next)) } catch {} }
  }

  return (
    <div className={['bg-raised border border-line-subtle rounded-md overflow-hidden', className ?? ''].join(' ')}>
      {collapsible ? (
        <button
          onClick={toggle}
          className={[
            'w-full flex items-center gap-2 px-4 py-3 text-sm font-semibold text-ink',
            'hover:bg-hovered transition-colors duration-fast text-left',
            open ? 'border-b border-line-subtle' : '',
          ].join(' ')}
        >
          <ChevronRight className={['h-4 w-4 shrink-0 text-ink-soft transition-transform duration-fast', open ? 'rotate-90' : ''].join(' ')} />
          {title}
        </button>
      ) : (
        <div className="px-4 py-3 border-b border-line-subtle">
          <p className="text-sm font-semibold text-ink">{title}</p>
        </div>
      )}
      {open && children}
    </div>
  )
}
