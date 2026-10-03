import { useEffect, useState } from 'react'
import { fetchApiStatus, type ApiStatus } from './api/health'
import { fetchMe, fetchOrganization, type Me, type Organization } from './identity/api'
import { DevUserSwitcher } from './identity/DevUserSwitcher'
import { getDevUser, setDevUser } from './identity/devUser'
import { KnowledgePage } from './knowledge/KnowledgePage'
import { ProjectsPage } from './projects/ProjectsPage'
import { TeamsPage } from './teams/TeamsPage'
import './App.css'

const statusText: Record<ApiStatus, string> = {
  checking: 'Verbindung wird geprüft …',
  ready: 'Backend bereit',
  unavailable: 'Backend nicht erreichbar',
}

const roleText: Record<Me['organizationRole'], string> = {
  admin: 'Organisations-Admin',
  member: 'Mitglied',
}

type Session = { me: Me; organization: Organization }

type Tab = 'projects' | 'knowledge' | 'teams'

const tabText: Record<Tab, string> = { projects: 'Projekte', knowledge: 'Wissen', teams: 'Teams' }

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')
  const [devUser, setDevUserState] = useState<string | null>(getDevUser)
  const [session, setSession] = useState<Session | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('projects')
  const [openArticle, setOpenArticle] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchApiStatus(controller.signal).then(setStatus, () => {})
    return () => controller.abort()
  }, [])

  useEffect(() => {
    let current = true
    Promise.all([fetchMe(), fetchOrganization()]).then(
      ([me, organization]) => current && setSession({ me, organization }),
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [devUser])

  function switchUser(objectId: string) {
    setDevUser(objectId)
    setSession(null)
    setError(null)
    setDevUserState(objectId)
  }

  return (
    <div className="app">
      <header className="app-header">
        <div>
          <h1>ProjectHub</h1>
          {session && <p className="organization">{session.organization.name}</p>}
        </div>
        <div className="account">
          {session && (
            <p>
              <strong>{session.me.displayName}</strong> · {roleText[session.me.organizationRole]}
            </p>
          )}
          {import.meta.env.DEV && <DevUserSwitcher current={devUser} onChange={switchUser} />}
        </div>
      </header>
      <main className="content">
        {error && <p role="alert">{error}</p>}
        {session && (
          <>
            <nav className="tabs" aria-label="Bereiche">
              {(Object.keys(tabText) as Tab[]).map((value) => (
                <button
                  key={value}
                  type="button"
                  className={value === tab ? 'tab active' : 'tab'}
                  aria-current={value === tab ? 'page' : undefined}
                  onClick={() => {
                    setTab(value)
                    setOpenArticle(null)
                  }}
                >
                  {tabText[value]}
                </button>
              ))}
            </nav>
            {tab === 'projects' && (
              <ProjectsPage
                key={session.me.id}
                me={session.me}
                onOpenArticle={(id) => {
                  setOpenArticle(id)
                  setTab('knowledge')
                }}
              />
            )}
            {tab === 'knowledge' && <KnowledgePage key={`${session.me.id}:${openArticle}`} me={session.me} initialArticleId={openArticle} />}
            {tab === 'teams' && <TeamsPage key={session.me.id} me={session.me} />}
          </>
        )}
      </main>
      <footer className="app-footer">
        <span className={`status status-${status}`} role="status">
          {statusText[status]}
        </span>
      </footer>
    </div>
  )
}

export default App
