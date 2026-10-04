import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect, useRef } from 'react'
import { devIdentityEnabled, getDevUser } from '../identity/devUser'

const hubPath = '/api/v1/hubs/notifications'

/**
 * Listens for changes to the signed-in person's notifications. Messages carry only the unread count;
 * the list is loaded through the API. Returns a function that stops listening.
 */
export function subscribeToNotifications(onChange: (unreadCount: number | null) => void): () => void {
  const devUser = devIdentityEnabled ? getDevUser() : null
  const url = devUser ? `${hubPath}?devUser=${encodeURIComponent(devUser)}` : hubPath
  const connection = new HubConnectionBuilder().withUrl(url).withAutomaticReconnect().configureLogging(LogLevel.Warning).build()

  connection.on('NotificationsChanged', (message: { unreadCount: number }) => onChange(message.unreadCount))
  // After a reconnect we may have missed messages: let the caller reload.
  connection.onreconnected(() => onChange(null))

  const started = connection.start().catch(() => {
    // Realtime is a convenience; the bell still loads on its own.
  })

  return () => {
    void started.finally(() => connection.stop())
  }
}

/** Calls <paramref name="onChange"/> with the new unread count, or null when the caller should reload. */
export function useNotificationEvents(onChange: (unreadCount: number | null) => void) {
  const handler = useRef(onChange)
  useEffect(() => {
    handler.current = onChange
  })

  useEffect(() => subscribeToNotifications((count) => handler.current(count)), [])
}
