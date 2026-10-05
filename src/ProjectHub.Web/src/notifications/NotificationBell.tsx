import { useCallback, useEffect, useRef, useState } from 'react'
import { useNotificationEvents } from '../realtime/notificationEvents'
import {
  fetchNotifications,
  fetchPreferences,
  fetchUnreadCount,
  markAllRead,
  markRead,
  savePreferences,
  type Notification,
  type NotificationPreferences,
} from './api'
import { EmptyState } from '../ui/EmptyState'

type Props = { onOpen: (notification: Notification) => void }

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

const unreadText = (count: number) => (count === 0 ? 'keine ungelesen' : count === 1 ? '1 ungelesen' : `${count} ungelesen`)

/** Bell with the unread count; opens the person's notifications and their settings. */
export function NotificationBell({ onOpen }: Props) {
  const [count, setCount] = useState(0)
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<Notification[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const button = useRef<HTMLButtonElement>(null)
  const container = useRef<HTMLDivElement>(null)

  const loadCount = useCallback(() => {
    fetchUnreadCount().then(
      (result) => setCount(result.count),
      () => {},
    )
  }, [])

  const loadList = useCallback(() => {
    fetchNotifications().then(
      (page) => setItems(page.items),
      (e: Error) => setError(e.message),
    )
  }, [])

  useEffect(loadCount, [loadCount])

  useNotificationEvents((unread) => {
    if (unread === null) loadCount()
    else setCount(unread)
    if (open) loadList()
  })

  function toggle() {
    if (!open) loadList()
    setOpen(!open)
  }

  async function run(action: () => Promise<void>) {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError((e as Error).message)
    }
    loadList()
    loadCount()
  }

  function openItem(notification: Notification) {
    if (!notification.readAt) void run(() => markRead(notification.id))
    setOpen(false)
    onOpen(notification)
  }

  // Escape or a click outside closes the panel, wherever the focus is (a disabled button drops it).
  useEffect(() => {
    if (!open) return
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (event.key !== 'Escape') return
      setOpen(false)
      button.current?.focus()
    }
    const onPointer = (event: PointerEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPointer)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer)
    }
  }, [open])

  return (
    <div className="notifications" ref={container}>
      <button
        ref={button}
        type="button"
        className="notification-bell"
        aria-expanded={open}
        aria-controls="notification-panel"
        aria-label={`Benachrichtigungen, ${unreadText(count)}`}
        onClick={toggle}
      >
        <span aria-hidden="true">🔔</span>
        {count > 0 && (
          <span className="notification-count" aria-hidden="true">
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>
      {open && (
        <section id="notification-panel" className="notification-panel" aria-labelledby="notification-heading">
          <header className="row">
            <h2 id="notification-heading">Benachrichtigungen</h2>
            <button type="button" className="link-button" disabled={count === 0} onClick={() => void run(markAllRead)}>
              Alle gelesen
            </button>
          </header>
          {error && <p role="alert">{error}</p>}
          {items === null ? (
            <p className="muted">Wird geladen …</p>
          ) : items.length === 0 ? (
            <EmptyState emoji="🔔">Keine Benachrichtigungen. Alles im Blick ✨</EmptyState>
          ) : (
            <ul className="notification-list">
              {items.map((notification) => (
                <li key={notification.id} className={notification.readAt ? 'notification' : 'notification unread'}>
                  <button type="button" className="notification-open" onClick={() => openItem(notification)}>
                    <span className="notification-title">{notification.title}</span>
                    {notification.body && <span className="notification-body">{notification.body}</span>}
                    <span className="muted notification-meta">
                      {!notification.readAt && <strong>Neu · </strong>}
                      {dateFormat.format(new Date(notification.createdAt))}
                    </span>
                  </button>
                  {!notification.readAt && (
                    <button
                      type="button"
                      className="link-button"
                      aria-label={`„${notification.title}“ als gelesen markieren`}
                      onClick={() => void run(() => markRead(notification.id))}
                    >
                      Gelesen
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
          <PreferencesForm />
        </section>
      )}
    </div>
  )
}

function PreferencesForm() {
  const [preferences, setPreferences] = useState<NotificationPreferences | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => {
    fetchPreferences().then(setPreferences, (e: Error) => setMessage(e.message))
  }, [])

  async function change(changes: Partial<NotificationPreferences>) {
    if (!preferences) return
    const next = { ...preferences, ...changes }
    setPreferences(next)
    setMessage(null)
    try {
      setPreferences(await savePreferences(next))
      setMessage('Gespeichert.')
    } catch (e) {
      setPreferences(preferences)
      setMessage((e as Error).message)
    }
  }

  return (
    <fieldset className="notification-preferences" disabled={!preferences}>
      <legend>Benachrichtigen</legend>
      <label>
        <input
          type="checkbox"
          checked={preferences?.inAppEnabled ?? true}
          onChange={(event) => void change({ inAppEnabled: event.target.checked })}
        />
        In der App
      </label>
      <label>
        <input
          type="checkbox"
          checked={preferences?.emailEnabled ?? true}
          onChange={(event) => void change({ emailEnabled: event.target.checked })}
        />
        Per Mail
      </label>
      {preferences?.webexAvailable && (
        <label>
          <input type="checkbox" checked={preferences.webexEnabled} onChange={(event) => void change({ webexEnabled: event.target.checked })} />
          Per Webex
        </label>
      )}
      {message && (
        <p className="muted" role="status">
          {message}
        </p>
      )}
    </fieldset>
  )
}
