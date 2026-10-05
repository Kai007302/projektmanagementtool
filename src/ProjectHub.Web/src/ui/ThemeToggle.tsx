import { useState } from 'react'
import { applyTheme, storedTheme, systemTheme, type Theme } from './theme'

/** Switches between light and dark mode; starts with the stored choice or the system setting. */
export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(() => storedTheme() ?? systemTheme())
  const next: Theme = theme === 'dark' ? 'light' : 'dark'
  return (
    <button
      type="button"
      className="icon-button"
      aria-label={next === 'dark' ? 'Dunkles Design einschalten' : 'Helles Design einschalten'}
      title={next === 'dark' ? 'Dunkles Design' : 'Helles Design'}
      onClick={() => {
        applyTheme(next)
        setTheme(next)
      }}
    >
      <span aria-hidden="true">{theme === 'dark' ? '☀️' : '🌙'}</span>
    </button>
  )
}
