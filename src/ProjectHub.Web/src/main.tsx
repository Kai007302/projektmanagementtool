import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { signIn } from './identity/signIn'
import { storedTheme } from './ui/theme'

// A stored light/dark choice wins over the system setting (index.css follows the system otherwise).
const theme = storedTheme()
if (theme) document.documentElement.dataset.theme = theme

const root = createRoot(document.getElementById('root')!)

// The app renders once the person is signed in; until then the browser may be on its way to Microsoft.
signIn().then(
  (signedIn) => {
    if (signedIn) {
      root.render(
        <StrictMode>
          <App />
        </StrictMode>,
      )
    }
  },
  (error: unknown) => {
    root.render(<p role="alert">Anmeldung fehlgeschlagen: {error instanceof Error ? error.message : String(error)}</p>)
  },
)
