import { useEffect, useState } from 'react'
import { fetchAiStatus, type AiStatus } from './ai/api'
import { AssistantPage } from './ai/AssistantPage'
import { fetchApiStatus, type ApiStatus } from './api/health'
import { fetchMe, fetchOrganization, type Me, type Organization } from './identity/api'
import { DevUserSwitcher } from './identity/DevUserSwitcher'
import { devIdentityEnabled, getDevUser, setDevUser } from './identity/devUser'
import { signedInWithEntra, signOut } from './identity/signIn'
import { KnowledgePage } from './knowledge/KnowledgePage'
import type { Notification } from './notifications/api'
import { NotificationBell } from './notifications/NotificationBell'
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

type Session = { me: Me; organization: Organization; ai: AiStatus | null }

type Tab = 'projects' | 'knowledge' | 'teams' | 'assistant'

const tabText: Record<Tab, string> = { projects: 'Projekte', knowledge: 'Wissen', teams: 'Teams', assistant: 'Assistent' }

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')
  const [devUser, setDevUserState] = useState<string | null>(getDevUser)
  const [session, setSession] = useState<Session | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('projects')
  const [openArticle, setOpenArticle] = useState<string | null>(null)
  // A project (and task) to open, e.g. from a notification; the counter remounts the page on every jump.
  const [openProject, setOpenProject] = useState<{ projectId: string; taskId: string | null; jump: number } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchApiStatus(controller.signal).then(setStatus, () => {})
    return () => controller.abort()
  }, [])

  useEffect(() => {
    let current = true
    Promise.all([fetchMe(), fetchOrganization(), fetchAiStatus().catch(() => null)]).then(
      ([me, organization, ai]) => current && setSession({ me, organization, ai }),
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [devUser])

  function openNotification(notification: Notification) {
    if (notification.resourceType === 'knowledge_article' && notification.resourceId) {
      setOpenArticle(notification.resourceId)
      setTab('knowledge')
    } else if (notification.projectId) {
      const taskId = notification.resourceType === 'task' ? notification.resourceId : null
      setOpenProject((current) => ({ projectId: notification.projectId!, taskId, jump: (current?.jump ?? 0) + 1 }))
      setTab('projects')
    }
  }

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
            <div className="account-row">
              <p>
                <strong>{session.me.displayName}</strong> · {roleText[session.me.organizationRole]}
              </p>
              <NotificationBell key={session.me.id} onOpen={openNotification} />
            </div>
          )}
          {devIdentityEnabled && <DevUserSwitcher current={devUser} onChange={switchUser} />}
          {signedInWithEntra() && (
            <button type="button" className="link-button" onClick={() => void signOut()}>
              Abmelden
            </button>
          )}
        </div>
      </header>
      <main className="content">
        {error && <p role="alert">{error}</p>}
        {session && (
          <>
            <nav className="tabs" aria-label="Bereiche">
              {(Object.keys(tabText) as Tab[]).filter((value) => value !== 'assistant' || session.ai?.enabled).map((value) => (
                <button
                  key={value}
                  type="button"
                  className={value === tab ? 'tab active' : 'tab'}
                  aria-current={value === tab ? 'page' : undefined}
                  onClick={() => {
                    setTab(value)
                    setOpenArticle(null)
                    setOpenProject(null)
                  }}
                >
                  {tabText[value]}
                </button>
              ))}
            </nav>
            {tab === 'projects' && (
              <ProjectsPage
                key={`${session.me.id}:${openProject?.jump ?? 0}`}
                me={session.me}
                initialProjectId={openProject?.projectId ?? null}
                initialTaskId={openProject?.taskId ?? null}
                onOpenArticle={(id) => {
                  setOpenArticle(id)
                  setTab('knowledge')
                }}
              />
            )}
            {tab === 'knowledge' && <KnowledgePage key={`${session.me.id}:${openArticle}`} me={session.me} initialArticleId={openArticle} />}
            {tab === 'teams' && <TeamsPage key={session.me.id} me={session.me} />}
            {tab === 'assistant' && session.ai?.enabled && (
              <AssistantPage
                key={session.me.id}
                status={session.ai}
                onOpenArticle={(id) => {
                  setOpenArticle(id)
                  setTab('knowledge')
                }}
              />
            )}
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
