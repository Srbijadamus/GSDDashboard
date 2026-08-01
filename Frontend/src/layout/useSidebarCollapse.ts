import { useState, useEffect } from 'react'

const KEY = 'gsd.sidebar.collapsed'

export function useSidebarCollapse() {
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    try { return localStorage.getItem(KEY) === 'true' } catch { return false }
  })

  useEffect(() => {
    try { localStorage.setItem(KEY, String(collapsed)) } catch {}
  }, [collapsed])

  return { collapsed, setCollapsed, toggle: () => setCollapsed(c => !c) }
}
