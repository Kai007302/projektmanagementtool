import { useState } from 'react'
import { applyTheme, storedTheme, systemTheme, type Theme } from './theme'

/** Light or dark mode: starts with the stored choice or the system setting; `toggle` switches and remembers it. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() => storedTheme() ?? systemTheme())
  const next: Theme = theme === 'dark' ? 'light' : 'dark'
  return {
    theme,
    /** Menu text for the switch, e.g. "Dunkles Design". */
    toggleLabel: next === 'dark' ? 'Dunkles Design' : 'Helles Design',
    toggle: () => {
      applyTheme(next)
      setTheme(next)
    },
  }
}
