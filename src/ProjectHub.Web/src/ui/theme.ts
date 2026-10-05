export type Theme = 'light' | 'dark'

const key = 'projecthub.theme'

/** The stored choice, or null to follow the system setting. */
export function storedTheme(): Theme | null {
  try {
    const value = localStorage.getItem(key)
    return value === 'light' || value === 'dark' ? value : null
  } catch {
    return null
  }
}

export function systemTheme(): Theme {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/** Applies a theme to the page and remembers it for this browser. */
export function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme
  try {
    localStorage.setItem(key, theme)
  } catch {
    // Storage may be blocked; the theme still applies for this visit.
  }
}
