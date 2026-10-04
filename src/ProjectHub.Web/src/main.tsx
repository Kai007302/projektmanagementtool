import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { signIn } from './identity/signIn'

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
