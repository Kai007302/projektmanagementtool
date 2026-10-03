import { useEffect, useState } from 'react'
import { fetchApiStatus, type ApiStatus } from './api/health'
import './App.css'

const statusText: Record<ApiStatus, string> = {
  checking: 'Verbindung wird geprüft …',
  ready: 'Backend bereit',
  unavailable: 'Backend nicht erreichbar',
}

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')

  useEffect(() => {
    const controller = new AbortController()
    fetchApiStatus(controller.signal).then(setStatus, () => {})
    return () => controller.abort()
  }, [])

  return (
    <main className="home">
      <h1>ProjectHub</h1>
      <p>Projektmanagement und Zusammenarbeit an einem Ort.</p>
      <p className={`status status-${status}`} role="status">
        {statusText[status]}
      </p>
    </main>
  )
}

export default App
