import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { fakeApi, json } from './test/fakeApi'

const org = { id: 'org-1', name: 'Contoso (Dev)', slug: 'contoso-dev' }
const general = { id: 'd-1', name: 'Allgemein', role: 'member' }
const ada = { id: 'u-ada', displayName: 'Ada Admin', email: 'ada@x', organizationId: 'org-1', organizationRole: 'admin', departments: [general] }
const eva = { id: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', organizationId: 'org-1', organizationRole: 'member', departments: [general] }
const ben = { ...eva, id: 'u-ben', displayName: 'Ben Leitung', departments: [{ id: 'd-2', name: 'Plattform', role: 'lead' }, general] }
const newcomer = { ...eva, id: 'u-neu', displayName: 'Neu Ohne Abteilung', departments: [] }
const devUsers = [
  { objectId: 'dev-ada', displayName: 'Ada Admin', organization: 'Contoso (Dev)', organizationRole: 'admin' },
  { objectId: 'dev-eva', displayName: 'Eva Viewer', organization: 'Contoso (Dev)', organizationRole: 'member' },
]
const platform = { id: 'd-2', name: 'Plattform', description: null, memberCount: 1, myRole: 'lead', canManage: true, entraGroupId: null as string | null, version: 1 }
const sales = { id: 'd-3', name: 'Vertrieb', description: null, memberCount: 2, myRole: null, canManage: false, entraGroupId: null, version: 1 }

function baseRoutes(me: typeof ada = ada) {
  return {
    'GET /health/ready': () => new Response('Healthy'),
    'GET /api/dev/users': () => json(devUsers),
    'GET /api/v1/me': (_: RequestInit | undefined, headers: Headers) =>
      json(headers.get('X-Dev-User') === 'dev-eva' ? eva : me),
    'GET /api/v1/organization': () => json(org),
    'GET /api/v1/departments?limit=100': () => json({ items: [platform, sales], nextOffset: null }),
    'GET /api/v1/departments/unassigned': () => json([]),
    'GET /api/v1/projects?limit=100': () => json({ items: [], nextOffset: null }),
    'GET /api/v1/projects?limit=100&departmentId=d-1': () => json({ items: [], nextOffset: null }),
    'GET /api/v1/projects?limit=100&departmentId=d-2': () => json({ items: [], nextOffset: null }),
    'GET /api/v1/me/notifications/unread-count': () => json({ count: 0 }),
  }
}

async function openAdministration() {
  await userEvent.click(await screen.findByRole('button', { name: 'Verwaltung' }))
}

describe('App', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows the signed-in user, organization and backend status', async () => {
    fakeApi(baseRoutes())
    render(<App />)

    expect(await screen.findByText('Contoso (Dev)', { selector: '.organization' })).toBeInTheDocument()
    expect(screen.getByText('Ada Admin', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByText(/Organisations-Admin/)).toBeInTheDocument()
    expect(await screen.findByText('Backend bereit')).toBeInTheDocument()
  })

  it('keeps the open area in the address and opens it again after reloading', async () => {
    fakeApi(baseRoutes())
    window.history.replaceState(null, '', '/verwaltung')
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Verwaltung', current: 'page' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Projekte' }))
    await waitFor(() => expect(window.location.pathname).toBe('/'))
  })

  it('opens the projects instead of an area the person cannot use', async () => {
    fakeApi(baseRoutes(eva))
    window.history.replaceState(null, '', '/verwaltung')
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Projekte', current: 'page' })).toBeInTheDocument()
    await waitFor(() => expect(window.location.pathname).toBe('/'))
  })

  it('reports an unavailable backend', async () => {
    fakeApi({ ...baseRoutes(), 'GET /health/ready': () => new Response('Unhealthy', { status: 503 }) })
    render(<App />)

    expect(await screen.findByText('Backend nicht erreichbar')).toBeInTheDocument()
  })

  it('starts with all departments and remembers the chosen one', async () => {
    const api = fakeApi(baseRoutes(ben))
    const { unmount } = render(<App />)

    const switcher = await screen.findByLabelText('Abteilung')
    expect(switcher).toHaveValue('')
    await userEvent.selectOptions(switcher, 'd-2')

    await waitFor(() => expect(api.calls.some((c) => c.key === 'GET /api/v1/projects?limit=100&departmentId=d-2')).toBe(true))
    expect(localStorage.getItem('projecthub.department.u-ben')).toBe('d-2')
    unmount()
    render(<App />)
    expect(await screen.findByLabelText('Abteilung')).toHaveValue('d-2')
  })

  it('offers the administration to admins and leads only', async () => {
    fakeApi(baseRoutes(eva))
    render(<App />)

    await screen.findByRole('button', { name: 'Projekte' })
    expect(screen.queryByRole('button', { name: 'Verwaltung' })).not.toBeInTheDocument()
  })

  it('shows leads only the departments they manage, without creating new ones', async () => {
    fakeApi({ ...baseRoutes(ben), 'GET /api/v1/departments/d-2': () => json({ ...platform, members: [] }) })
    render(<App />)
    await openAdministration()

    const list = await screen.findByRole('list', { name: 'Abteilungen' })
    expect(within(list).getByRole('button', { name: /Plattform/ })).toBeInTheDocument()
    expect(within(list).queryByRole('button', { name: /Vertrieb/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Abteilung' })).not.toBeInTheDocument()
  })

  it('creates a department with its Entra group', async () => {
    let departments = [platform]
    const api = fakeApi({
      ...baseRoutes(),
      'GET /api/v1/departments?limit=100': () => json({ items: departments, nextOffset: null }),
      'GET /api/v1/departments/d-2': () => json({ ...platform, members: [] }),
      'GET /api/v1/departments/d-9': () => json({ ...platform, id: 'd-9', name: 'Einkauf', members: [] }),
      'POST /api/v1/departments': (init) => {
        const body = JSON.parse(String(init?.body)) as { name: string; entraGroupId: string }
        const department = { ...platform, id: 'd-9', name: body.name, entraGroupId: body.entraGroupId, memberCount: 0 }
        departments = [...departments, department]
        return json(department, 201)
      },
    })
    render(<App />)
    await openAdministration()

    await userEvent.click(await screen.findByRole('button', { name: '+ Abteilung' }))
    await userEvent.type(screen.getByLabelText('Name der Abteilung'), 'Einkauf')
    await userEvent.type(screen.getByLabelText('Entra-Gruppe (Objekt-ID, optional)'), 'group-1')
    await userEvent.click(screen.getByRole('button', { name: 'Abteilung anlegen' }))

    const list = await screen.findByRole('list', { name: 'Abteilungen' })
    expect(await within(list).findByRole('button', { name: /Einkauf/ })).toBeInTheDocument()
    const post = api.calls.find((c) => c.key === 'POST /api/v1/departments')
    expect(JSON.parse(String(post?.init?.body))).toEqual({ name: 'Einkauf', description: null, entraGroupId: 'group-1' })
  })

  it('shows department members and lets managers add someone', async () => {
    let members = [{ userId: 'u-ben', displayName: 'Ben Leitung', email: 'ben@x', role: 'lead', source: 'manual' }]
    fakeApi({
      ...baseRoutes(),
      'GET /api/v1/departments/d-2': () => json({ ...platform, members }),
      'GET /api/v1/users?limit=100': () =>
        json({
          items: [
            { id: 'u-ben', displayName: 'Ben Leitung', email: 'ben@x', department: null, status: 'active' },
            { id: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', department: null, status: 'active' },
          ],
          nextOffset: null,
        }),
      'POST /api/v1/departments/d-2/members': (init) => {
        const body = JSON.parse(String(init?.body)) as { userId: string; role: string }
        members = [...members, { userId: body.userId, displayName: 'Eva Viewer', email: 'eva@x', role: body.role, source: 'manual' }]
        return new Response(null, { status: 204 })
      },
    })
    render(<App />)
    await openAdministration()

    const details = await screen.findByRole('region', { name: 'Plattform' })
    expect(within(details).getByText('Ben Leitung')).toBeInTheDocument()
    await userEvent.click(within(details).getByRole('button', { name: '+ Person hinzufügen' }))

    const person = await screen.findByLabelText('Person')
    await within(person).findByRole('option', { name: 'Eva Viewer' })
    expect(within(person).queryByRole('option', { name: 'Ben Leitung' })).not.toBeInTheDocument()
    await userEvent.selectOptions(person, 'u-eva')
    await userEvent.click(screen.getByRole('button', { name: 'Hinzufügen' }))

    expect(await within(details).findByText('Eva Viewer')).toBeInTheDocument()
  })

  it('lets admins and leads take in people without a department', async () => {
    const waiting = { id: 'u-neu', displayName: 'Neu Ohne Abteilung', email: 'neu@x', department: null, status: 'active' }
    let unassigned = [waiting]
    const api = fakeApi({
      ...baseRoutes(),
      'GET /api/v1/departments/unassigned': () => json(unassigned),
      'GET /api/v1/departments/d-2': () => json({ ...platform, members: [] }),
      'POST /api/v1/departments/d-2/members': () => {
        unassigned = []
        return new Response(null, { status: 204 })
      },
    })
    render(<App />)
    await openAdministration()

    const panel = await screen.findByRole('region', { name: 'Ohne Abteilung' })
    await userEvent.click(within(panel).getByRole('button', { name: 'Aufnehmen' }))

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Ohne Abteilung' })).not.toBeInTheDocument())
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'POST /api/v1/departments/d-2/members')?.init?.body))).toEqual({
      userId: 'u-neu',
      role: 'member',
    })
  })

  it('tells people without a department that someone will take them in', async () => {
    fakeApi(baseRoutes(newcomer))
    render(<App />)

    expect(await screen.findByText('Du bist noch keiner Abteilung zugeordnet.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Abteilung')).not.toBeInTheDocument()
  })

  it('switches the development user and sends it to the API', async () => {
    const api = fakeApi(baseRoutes())
    render(<App />)

    const switcher = await screen.findByLabelText(/Dev-Anmeldung als/)
    await within(switcher).findByRole('option', { name: /Eva Viewer/ })
    await userEvent.selectOptions(switcher, 'dev-eva')

    expect(await screen.findByText('Eva Viewer', { selector: 'strong' })).toBeInTheDocument()
    expect(localStorage.getItem('projecthub.devUser')).toBe('dev-eva')
    expect(api.calls.some((c) => c.key === 'GET /api/v1/me' && c.headers.get('X-Dev-User') === 'dev-eva')).toBe(true)
  })
})
