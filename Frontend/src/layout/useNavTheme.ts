import { useState, useEffect } from 'react'

const KEY = 'gsd.nav.theme'

export function useNavTheme() {
  const [navTheme, setNavTheme] = useState<'dark' | 'light'>(() => {
    try {
      return localStorage.getItem(KEY) === 'light' ? 'light' : 'dark'
    } catch {
      return 'dark'
    }
  })

  useEffect(() => {
    document.documentElement.setAttribute('data-nav-theme', navTheme)
    try { localStorage.setItem(KEY, navTheme) } catch {}
  }, [navTheme])

  return {
    navTheme,
    toggle: () => setNavTheme(t => t === 'dark' ? 'light' : 'dark'),
  }
}
