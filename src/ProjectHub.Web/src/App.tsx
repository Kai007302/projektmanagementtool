import { useEffect, useState } from 'react'
import { fetchAiStatus, type AiStatus } from './ai/api'
import { AssistantPage } from './ai/AssistantPage'
import { fetchApiStatus, type ApiStatus } from './api/health'
import { AdminPage } from './departments/AdminPage'
import { managesAnything, readDepartment, rememberDepartment } from './departments/currentDepartment'
import { DepartmentSwitcher } from './departments/DepartmentSwitcher'
import { fetchMe, fetchOrganization, type Me, type Organization } from './identity/api'
import { DevUserSwitcher } from './identity/DevUserSwitcher'
import { devIdentityEnabled, getDevUser, setDevUser } from './identity/devUser'
import { AccountMenu } from './identity/AccountMenu'
import { signedInWithEntra, signOut } from './identity/signIn'
import { createArticle } from './knowledge/api'
import { KnowledgePage } from './knowledge/KnowledgePage'
import { areaPath, currentPlace, showPath, type Area, type KnowledgeMode, type ProjectView } from './navigation/location'
import type { Notification } from './notifications/api'
import { NotificationBell } from './notifications/NotificationBell'
import { LegalFooter } from './privacy/LegalFooter'
import { createProject } from './projects/api'
import { ProjectsPage } from './projects/ProjectsPage'
import { CommandPalette } from './ui/CommandPalette'
import { toast } from './ui/toast'
import { Toaster } from './ui/Toaster'
import './App.css'
import { SearchIcon } from './ui/icons'

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

type Tab = Area

const tabText: Record<Tab, string> = { projects: 'Projekte', knowledge: 'Galaxie', admin: 'Verwaltung', assistant: 'Assistent' }

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')
  const [devUser, setDevUserState] = useState<string | null>(getDevUser)
  const [session, setSession] = useState<Session | null>(null)
  const [error, setError] = useState<string | null>(null)
  // The page in the address when the app starts (e.g. after reloading the browser) is the page shown first.
  const [start] = useState(currentPlace)
  const [tab, setTab] = useState<Tab>(start.area)
  const [openArticle, setOpenArticle] = useState<string | null>(start.articleId)
  const [knowledgeMode, setKnowledgeMode] = useState<KnowledgeMode | null>(start.knowledgeMode)
  // A project (and task) to open, e.g. from a notification; the counter remounts the page on every jump.
  const [openProject, setOpenProject] = useState<{ projectId: string; taskId: string | null; view: ProjectView | null; jump: number } | null>(
    start.projectId ? { projectId: start.projectId, taskId: null, view: start.view, jump: 0 } : null,
  )
  // Counts clicks on the areas in the header: each one starts the area afresh, also when a project from the address is open.
  const [visit, setVisit] = useState(0)
  const [paletteOpen, setPaletteOpen] = useState(false)
  // The department whose projects and knowledge are shown; '' for all (ADR 0021).
  const [department, setDepartment] = useState('')
  const [meRevision, setMeRevision] = useState(0)

  // Ctrl+K (Cmd+K on a Mac) opens the search from anywhere, like in Linear or Notion.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setPaletteOpen((open) => !open)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    fetchApiStatus(controller.signal).then(setStatus, () => {})
    return () => controller.abort()
  }, [])

  useEffect(() => {
    let current = true
    Promise.all([fetchMe(), fetchOrganization(), fetchAiStatus().catch(() => null)]).then(
      ([me, organization, ai]) => {
        if (!current) return
        setSession({ me, organization, ai })
        setDepartment(readDepartment(me))
      },
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [devUser])

  // Department memberships changed in the administration: reload who I am, keeping the page as it is.
  useEffect(() => {
    if (meRevision === 0) return
    let current = true
    fetchMe().then((me) => current && setSession((s) => (s ? { ...s, me } : s)), () => {})
    return () => {
      current = false
    }
  }, [meRevision])

  function openNotification(notification: Notification) {
    if (notification.resourceType === 'user') {
      goTo('admin')
    } else if (notification.resourceType === 'knowledge_article' && notification.resourceId) {
      showArticle(notification.resourceId)
    } else if (notification.projectId) {
      showProject(notification.projectId, notification.resourceType === 'task' ? notification.resourceId : null)
    }
  }

  function goTo(value: Tab) {
    setTab(value)
    setVisit((v) => v + 1)
    setOpenArticle(null)
    setKnowledgeMode(null)
    setOpenProject(null)
  }

  function showProject(projectId: string, taskId: string | null = null) {
    setOpenProject((current) => ({ projectId, taskId, view: null, jump: (current?.jump ?? 0) + 1 }))
    setTab('projects')
  }

  /** "Projekt … anlegen" and "Artikel … anlegen" from the search: create, then open what was created. */
  async function createFromSearch<T>(create: () => Promise<T>, open: (created: T) => void, done: string) {
    try {
      open(await create())
      toast(done)
    } catch (e) {
      toast((e as Error).message)
    }
  }

  function showArticle(articleId: string) {
    setOpenArticle(articleId)
    setTab('knowledge')
  }

  const tabs = session
    ? (Object.keys(tabText) as Tab[]).filter(
        (value) => (value !== 'assistant' || session.ai?.enabled) && (value !== 'admin' || managesAnything(session.me)),
      )
    : []

  // An area I cannot open (e.g. an old link to the administration) shows the projects instead.
  const shown: Tab = !session || tabs.includes(tab) ? tab : 'projects'

  // The pages write their own part of the address (project, view, article); this only switches the area.
  useEffect(() => {
    if (currentPlace().area !== shown) showPath(areaPath(shown))
  }, [shown])

  function switchDepartment(departmentId: string) {
    if (!session) return
    rememberDepartment(session.me, departmentId)
    setDepartment(departmentId)
    setOpenArticle(null)
    setOpenProject(null)
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
        <div className="app-header-inner">
          <div className="brand">
            <img className="brand-mark" src="/favicon.svg" alt="" width={32} height={32} />
            <div>
              <h1>ProjectHub</h1>
              {session && <p className="organization">{session.organization.name}</p>}
            </div>
          </div>
          {session && (
            <nav className="main-nav" aria-label="Bereiche">
              {tabs.map((value) => (
                <button
                  key={value}
                  type="button"
                  className={value === shown ? 'main-nav-item active' : 'main-nav-item'}
                  aria-current={value === shown ? 'page' : undefined}
                  onClick={() => goTo(value)}
                >
                  {tabText[value]}
                </button>
              ))}
            </nav>
          )}
          {session && (
            <div className="header-search">
              <button type="button" className="search-trigger" aria-label="Suchen (Strg+K)" onClick={() => setPaletteOpen(true)}>
                <SearchIcon />
                <kbd aria-hidden="true">Strg K</kbd>
              </button>
            </div>
          )}
          <div className="account">
            {devIdentityEnabled && <DevUserSwitcher current={devUser} onChange={switchUser} />}
            {session && (
              <div className="account-row">
                <DepartmentSwitcher me={session.me} value={department} onChange={switchDepartment} />
                <NotificationBell key={session.me.id} onOpen={openNotification} />
                <AccountMenu me={session.me} role={roleText[session.me.organizationRole]} onSignOut={signedInWithEntra() ? () => void signOut() : undefined} />
              </div>
            )}
            {!session && signedInWithEntra() && (
              <button type="button" onClick={() => void signOut()}>
                Abmelden
              </button>
            )}
          </div>
        </div>
      </header>
      <main className="content">
        {error && <p role="alert">{error}</p>}
        {session && session.me.departments.length === 0 && session.me.organizationRole !== 'admin' && (
          <div className="notice" role="status">
            <strong>Du bist noch keiner Abteilung zugeordnet.</strong> Die Abteilungsleitungen und Admins wissen Bescheid und nehmen dich auf.
            Bis dahin siehst du die Projekte, zu denen du eingeladen bist, und was für die ganze Organisation freigegeben ist.
          </div>
        )}
        {session && (
          <>
            {shown === 'projects' && (
              <ProjectsPage
                key={`${session.me.id}:${department}:${openProject?.jump ?? 0}:${visit}`}
                me={session.me}
                departmentId={department}
                initialProjectId={openProject?.projectId ?? null}
                initialTaskId={openProject?.taskId ?? null}
                initialView={openProject?.view ?? null}
                onOpenArticle={showArticle}
              />
            )}
            {shown === 'knowledge' && (
              <KnowledgePage
                key={`${session.me.id}:${department}:${openArticle}`}
                me={session.me}
                departmentId={department}
                initialArticleId={openArticle}
                initialMode={knowledgeMode ?? 'galaxy'}
              />
            )}
            {shown === 'admin' && managesAnything(session.me) && (
              <AdminPage key={session.me.id} me={session.me} onChanged={() => setMeRevision((r) => r + 1)} />
            )}
            {shown === 'assistant' && session.ai?.enabled && (
              <AssistantPage
                key={session.me.id}
                status={session.ai}
                onOpenArticle={showArticle}
              />
            )}
          </>
        )}
      </main>
      {session && paletteOpen && (
        <CommandPalette
          destinations={tabs.map((value) => ({ label: tabText[value], go: () => goTo(value) }))}
          onOpenProject={(id) => showProject(id)}
          onOpenArticle={showArticle}
          onCreateProject={(name) => void createFromSearch(() => createProject(name, '', department), (project) => showProject(project.id), `Projekt „${name}“ angelegt.`)}
          onCreateArticle={(title) =>
            void createFromSearch(
              () => createArticle({ title, articleType: 'article', summary: '', spaceId: null, visibility: 'department', departmentId: department || null }),
              (created) => showArticle(created.article.id),
              `Artikel „${title}“ als Entwurf angelegt.`,
            )
          }
          onClose={() => setPaletteOpen(false)}
        />
      )}
      <footer className="app-footer">
        <span className={`status status-${status}`} role="status">
          {statusText[status]}
        </span>
        <LegalFooter />
      </footer>
      <Toaster />
    </div>
  )
}

export default App
