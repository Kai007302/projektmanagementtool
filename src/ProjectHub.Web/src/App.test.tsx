import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { fakeApi, json } from './test/fakeApi'

const org = { id: 'org-1', name: 'Contoso (Dev)', slug: 'contoso-dev' }
const ada = { id: 'u-ada', displayName: 'Ada Admin', email: 'ada@x', organizationId: 'org-1', organizationRole: 'admin' }
const eva = { id: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', organizationId: 'org-1', organizationRole: 'member' }
const devUsers = [
  { objectId: 'dev-ada', displayName: 'Ada Admin', organization: 'Contoso (Dev)', organizationRole: 'admin' },
  { objectId: 'dev-eva', displayName: 'Eva Viewer', organization: 'Contoso (Dev)', organizationRole: 'member' },
]
const platform = { id: 't-1', name: 'Plattform', description: null, memberCount: 1 }

function baseRoutes(me = ada) {
  return {
    'GET /health/ready': () => new Response('Healthy'),
    'GET /api/dev/users': () => json(devUsers),
    'GET /api/v1/me': (_: RequestInit | undefined, headers: Headers) =>
      json(headers.get('X-Dev-User') === 'dev-eva' ? eva : me),
    'GET /api/v1/organization': () => json(org),
    'GET /api/v1/teams?limit=100': () => json({ items: [platform], nextOffset: null }),
  }
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

  it('reports an unavailable backend', async () => {
    fakeApi({ ...baseRoutes(), 'GET /health/ready': () => new Response('Unhealthy', { status: 503 }) })
    render(<App />)

    expect(await screen.findByText('Backend nicht erreichbar')).toBeInTheDocument()
  })

  it('lists the teams of the organization', async () => {
    fakeApi(baseRoutes())
    render(<App />)

    expect(await screen.findByRole('button', { name: /Plattform/ })).toBeInTheDocument()
  })

  it('offers team creation to organization admins only', async () => {
    fakeApi(baseRoutes(eva))
    render(<App />)

    await screen.findByRole('button', { name: /Plattform/ })
    expect(screen.queryByRole('button', { name: 'Team anlegen' })).not.toBeInTheDocument()
  })

  it('creates a team and reloads the list', async () => {
    let teams = [platform]
    const api = fakeApi({
      ...baseRoutes(),
      'GET /api/v1/teams?limit=100': () => json({ items: teams, nextOffset: null }),
      'POST /api/v1/teams': (init) => {
        const body = JSON.parse(String(init?.body)) as { name: string }
        const team = { id: 't-2', name: body.name, description: null, memberCount: 0 }
        teams = [...teams, team]
        return json(team, 201)
      },
    })
    render(<App />)

    await userEvent.type(await screen.findByLabelText('Teamname'), 'Vertrieb')
    await userEvent.click(screen.getByRole('button', { name: 'Team anlegen' }))

    expect(await screen.findByRole('button', { name: /Vertrieb/ })).toBeInTheDocument()
    expect(api.calls.some((c) => c.key === 'POST /api/v1/teams')).toBe(true)
  })

  it('shows the error message when team creation is rejected', async () => {
    fakeApi({
      ...baseRoutes(),
      'POST /api/v1/teams': () => json({ title: 'Conflict', detail: 'A team with this name already exists.' }, 409),
    })
    render(<App />)

    await userEvent.type(await screen.findByLabelText('Teamname'), 'Plattform')
    await userEvent.click(screen.getByRole('button', { name: 'Team anlegen' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('A team with this name already exists.')
  })

  it('shows team members and lets managers add someone', async () => {
    let members = [{ userId: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', role: 'owner' }]
    fakeApi({
      ...baseRoutes(),
      'GET /api/v1/teams/t-1': () => json({ ...platform, canManage: true, members }),
      'GET /api/v1/users?limit=100': () =>
        json({
          items: [
            { id: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', department: null, status: 'active' },
            { id: 'u-eva', displayName: 'Eva Viewer', email: 'eva@x', department: null, status: 'active' },
          ],
          nextOffset: null,
        }),
      'POST /api/v1/teams/t-1/members': (init) => {
        const body = JSON.parse(String(init?.body)) as { userId: string; role: string }
        members = [...members, { userId: body.userId, displayName: 'Eva Viewer', email: 'eva@x', role: body.role }]
        return new Response(null, { status: 204 })
      },
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: /Plattform/ }))
    const details = await screen.findByRole('heading', { name: 'Plattform' }).then((h) => h.parentElement!)
    expect(within(details).getByText('Ben Projektleiter')).toBeInTheDocument()

    const person = within(details).getByLabelText('Person')
    await within(person).findByRole('option', { name: 'Eva Viewer' })
    expect(within(person).queryByRole('option', { name: 'Ben Projektleiter' })).not.toBeInTheDocument()
    await userEvent.selectOptions(person, 'u-eva')
    await userEvent.click(within(details).getByRole('button', { name: 'Hinzufügen' }))

    expect(await within(details).findByText('Eva Viewer')).toBeInTheDocument()
  })

  it('hides member management from people who cannot manage the team', async () => {
    fakeApi({
      ...baseRoutes(eva),
      'GET /api/v1/teams/t-1': () =>
        json({ ...platform, canManage: false, members: [{ userId: 'u-ben', displayName: 'Ben', email: 'b', role: 'owner' }] }),
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: /Plattform/ }))
    await screen.findByRole('heading', { name: 'Plattform' })

    expect(screen.queryByRole('button', { name: 'Entfernen' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Person')).not.toBeInTheDocument()
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
