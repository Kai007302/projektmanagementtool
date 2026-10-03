import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect, useRef } from 'react'
import { getDevUser } from '../identity/devUser'

export type ProjectChange = { projectId: string; area: 'tasks' | 'board' | 'gantt' }

const hubPath = '/api/v1/hubs/projects'

/**
 * Listens for changes in a project made by anyone (ADR 0003). Messages only say that something
 * changed; the caller reloads through the API. Returns a function that stops listening.
 */
export function subscribeToProject(projectId: string, onChange: (change: ProjectChange) => void): () => void {
  // Browsers cannot send headers on WebSockets, so the development identity goes into the query.
  const devUser = import.meta.env.DEV ? getDevUser() : null
  const url = devUser ? `${hubPath}?devUser=${encodeURIComponent(devUser)}` : hubPath
  const connection = new HubConnectionBuilder()
    .withUrl(url)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  let stopped = false
  const join = () => connection.invoke('JoinProject', projectId)

  connection.on('ProjectChanged', (change: ProjectChange) => {
    if (change.projectId === projectId) onChange(change)
  })
  // After a reconnect we may have missed messages: rejoin and reload once.
  connection.onreconnected(() => {
    void join().then(() => onChange({ projectId, area: 'board' }))
  })

  const started = connection
    .start()
    .then(() => (stopped ? undefined : join()))
    .catch(() => {
      // Realtime is a convenience; without it the view still works and reloads on its own actions.
    })

  return () => {
    stopped = true
    // Stopping during negotiation is reported as an error, so let a pending start finish first.
    void started.finally(() => connection.stop())
  }
}

/** Calls <paramref name="onChange"/> whenever someone changes the project. */
export function useProjectEvents(projectId: string, onChange: (change: ProjectChange) => void) {
  const handler = useRef(onChange)
  useEffect(() => {
    handler.current = onChange
  })

  useEffect(() => subscribeToProject(projectId, (change) => handler.current(change)), [projectId])
}
