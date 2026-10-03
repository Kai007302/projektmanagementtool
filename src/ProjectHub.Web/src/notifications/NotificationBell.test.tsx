import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useNotificationEvents } from '../realtime/notificationEvents'
import { fakeApi, json } from '../test/fakeApi'
import type { Notification } from './api'
import { NotificationBell } from './NotificationBell'

const assigned: Notification = {
  id: 'n-1',
  type: 'task_assigned',
  title: 'Ben hat dir „Texte“ zugewiesen',
  body: null,
  resourceType: 'task',
  resourceId: 't-1',
  projectId: 'p-1',
  createdAt: '2026-10-03T10:00:00Z',
  readAt: null,
}

const mentioned: Notification = {
  ...assigned,
  id: 'n-2',
  type: 'knowledge_comment_mention',
  title: 'Clara hat dich im Artikel „Kickoff“ erwähnt',
  body: 'Schau mal',
  resourceType: 'knowledge_article',
  resourceId: 'a-1',
  projectId: null,
  readAt: '2026-10-03T11:00:00Z',
}

function routes(unread = 1) {
  return {
    'GET /api/v1/me/notifications/unread-count': () => json({ count: unread }),
    'GET /api/v1/me/notifications?limit=30': () => json({ items: [assigned, mentioned], nextOffset: null }),
    'GET /api/v1/me/notification-preferences': () => json({ inAppEnabled: true, emailEnabled: true }),
    'POST /api/v1/notifications/n-1/read': () => new Response(null, { status: 204 }),
    'POST /api/v1/me/notifications/read-all': () => new Response(null, { status: 204 }),
    'PUT /api/v1/me/notification-preferences': (init: RequestInit | undefined) => json(JSON.parse(String(init?.body))),
  }
}

const bell = () => screen.findByRole('button', { name: /^Benachrichtigungen, / })

describe('NotificationBell', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the unread count and lists notifications', async () => {
    fakeApi(routes(3))
    render(<NotificationBell onOpen={vi.fn()} />)

    await vi.waitFor(async () => expect(await bell()).toHaveAccessibleName('Benachrichtigungen, 3 ungelesen'))
    await userEvent.click(await bell())

    const panel = screen.getByRole('region', { name: 'Benachrichtigungen' })
    const items = await within(panel).findAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveClass('unread')
    expect(within(items[1]).getByText('Schau mal')).toBeInTheDocument()
    expect(within(items[1]).queryByRole('button', { name: /als gelesen markieren/ })).not.toBeInTheDocument()
  })

  it('opens a notification and marks it read', async () => {
    const api = fakeApi(routes())
    const onOpen = vi.fn()
    render(<NotificationBell onOpen={onOpen} />)

    await userEvent.click(await bell())
    await userEvent.click(await screen.findByRole('button', { name: /^Ben hat dir „Texte“ zugewiesen/ }))

    expect(onOpen).toHaveBeenCalledWith(assigned)
    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/notifications/n-1/read')).toBe(true))
    expect(screen.queryByRole('region', { name: 'Benachrichtigungen' })).not.toBeInTheDocument()
  })

  it('marks single or all notifications read', async () => {
    const api = fakeApi(routes())
    render(<NotificationBell onOpen={vi.fn()} />)

    await userEvent.click(await bell())
    await userEvent.click(await screen.findByRole('button', { name: '„Ben hat dir „Texte“ zugewiesen“ als gelesen markieren' }))
    await userEvent.click(screen.getByRole('button', { name: 'Alle gelesen' }))

    await vi.waitFor(() => expect(api.calls.some((c) => c.key === 'POST /api/v1/me/notifications/read-all')).toBe(true))
    expect(api.calls.some((c) => c.key === 'POST /api/v1/notifications/n-1/read')).toBe(true)
  })

  it('updates the counter live and closes with Escape', async () => {
    fakeApi(routes(0))
    render(<NotificationBell onOpen={vi.fn()} />)
    await vi.waitFor(async () => expect(await bell()).toHaveAccessibleName('Benachrichtigungen, keine ungelesen'))

    const [onChange] = vi.mocked(useNotificationEvents).mock.lastCall!
    act(() => onChange(2))

    expect(await bell()).toHaveAccessibleName('Benachrichtigungen, 2 ungelesen')
    await userEvent.click(await bell())
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('region', { name: 'Benachrichtigungen' })).not.toBeInTheDocument()
    expect(await bell()).toHaveFocus()
  })

  it('saves the channel settings', async () => {
    const api = fakeApi(routes())
    render(<NotificationBell onOpen={vi.fn()} />)

    await userEvent.click(await bell())
    const mail = await screen.findByRole('checkbox', { name: 'Per Mail' })
    await vi.waitFor(() => expect(mail).toBeEnabled())
    await userEvent.click(mail)

    expect(await screen.findByText('Gespeichert.')).toBeInTheDocument()
    expect(JSON.parse(String(api.calls.find((c) => c.key === 'PUT /api/v1/me/notification-preferences')?.init?.body))).toEqual({
      inAppEnabled: true,
      emailEnabled: false,
    })
    expect(mail).not.toBeChecked()
  })
})
