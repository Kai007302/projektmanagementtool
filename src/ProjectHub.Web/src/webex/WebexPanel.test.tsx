import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ProjectDetails } from '../projects/api'
import { fakeApi, json } from '../test/fakeApi'
import { isWebexUrl, type WebexLink } from './api'
import { WebexPanel } from './WebexPanel'

const meeting: WebexLink = {
  id: 'l-1',
  kind: 'meeting',
  title: 'Jour fixe',
  url: 'https://contoso.webex.com/meet/ben',
  status: 'active',
  createdByBot: false,
  createdAt: '2026-10-03T10:00:00Z',
}

const space: WebexLink = { ...meeting, id: 'l-2', kind: 'space', title: 'Intranet', url: 'webexteams://im?space=x', createdByBot: true, status: 'disconnected' }

function project(canEdit: boolean) {
  return { id: 'p-1', capabilities: { canContribute: canEdit, canEdit, canManage: canEdit }, members: [] } as unknown as ProjectDetails
}

describe('WebexPanel', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows links, opens them in a new window and marks a disconnected space', async () => {
    fakeApi({ 'GET /api/v1/projects/p-1/webex': () => json({ available: true, links: [meeting, space] }) })

    render(<WebexPanel project={project(false)} revision={0} onChanged={() => {}} />)

    const link = await screen.findByRole('link', { name: 'Meeting „Jour fixe“ in Webex öffnen (neues Fenster)' })
    expect(link).toHaveAttribute('href', meeting.url)
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(screen.getByText(/getrennt/)).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Webex-Link hinzufügen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Projektraum in Webex anlegen' })).not.toBeInTheDocument()
  })

  it('lets editors add a link and create the project space', async () => {
    const { calls } = fakeApi({
      'GET /api/v1/projects/p-1/webex': () => json({ available: true, links: [] }),
      'POST /api/v1/projects/p-1/webex/links': () => json(meeting, 201),
      'POST /api/v1/projects/p-1/webex/space': () => json(space, 201),
    })
    const onChanged = vi.fn()
    render(<WebexPanel project={project(true)} revision={0} onChanged={onChanged} />)

    const form = await screen.findByRole('form', { name: 'Webex-Link hinzufügen' })
    await userEvent.type(screen.getByLabelText('Titel'), 'Jour fixe')
    await userEvent.type(screen.getByLabelText('Link'), 'https://evil.example/meet')
    await userEvent.click(screen.getByRole('button', { name: 'Hinzufügen' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('webex.com')
    expect(calls.some((c) => c.key === 'POST /api/v1/projects/p-1/webex/links')).toBe(false)

    await userEvent.clear(screen.getByLabelText('Link'))
    await userEvent.type(screen.getByLabelText('Link'), meeting.url)
    await userEvent.click(screen.getByRole('button', { name: 'Hinzufügen' }))
    const added = calls.find((c) => c.key === 'POST /api/v1/projects/p-1/webex/links')
    expect(JSON.parse(String(added?.init?.body))).toEqual({ kind: 'meeting', title: 'Jour fixe', url: meeting.url })

    await userEvent.click(screen.getByRole('button', { name: 'Projektraum in Webex anlegen' }))
    expect(calls.some((c) => c.key === 'POST /api/v1/projects/p-1/webex/space')).toBe(true)
    expect(onChanged).toHaveBeenCalledTimes(2)
    expect(form).toBeInTheDocument()
  })

  it('stays hidden when Webex is not set up and nothing is linked', async () => {
    const { calls } = fakeApi({ 'GET /api/v1/projects/p-1/webex': () => json({ available: false, links: [] }) })

    const { container } = render(<WebexPanel project={project(true)} revision={0} onChanged={() => {}} />)

    await vi.waitFor(() => expect(calls).toHaveLength(1))
    await vi.waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('accepts only https links to webex.com', () => {
    expect(isWebexUrl('https://contoso.webex.com/meet/ben')).toBe(true)
    expect(isWebexUrl('https://webex.com.evil.example/')).toBe(false)
    expect(isWebexUrl('http://contoso.webex.com/')).toBe(false)
    expect(isWebexUrl('javascript:alert(1)')).toBe(false)
  })
})
