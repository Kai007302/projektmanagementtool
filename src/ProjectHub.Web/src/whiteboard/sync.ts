import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import * as Y from 'yjs'
import { devIdentityEnabled, getDevUser } from '../identity/devUser'
import { hubConnectionOptions } from '../identity/signIn'

const hubPath = '/api/v1/hubs/whiteboards'

/** Changes from the server are applied with this origin, so they are not sent back. */
export const remoteOrigin = Symbol('remote')

/** Local changes are collected this long and sent as one merged update (dragging produces many). */
const pushDelay = 60

export type Peer = { connectionId: string; userId: string; name: string; x: number | null; y: number | null; selectedObjectId: string | null }

export type SyncStatus = 'connecting' | 'online' | 'offline'

export type SyncHandlers = {
  onStatus: (status: SyncStatus, canEdit: boolean) => void
  onPeer: (peer: Peer) => void
  onPeerLeft: (connectionId: string) => void
  /** The server refused a change or the connection failed; the caller reconnects with a fresh document. */
  onRejected: (message: string) => void
}

export type WhiteboardSync = {
  sendPresence: (x: number | null, y: number | null, selectedObjectId: string | null) => void
  stop: () => void
}

const toBase64 = (bytes: Uint8Array) => {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

const fromBase64 = (text: string) => Uint8Array.from(atob(text), (c) => c.charCodeAt(0))

/**
 * Connects a Yjs document to the whiteboard hub (ADR 0009): only updates travel, never the whole canvas.
 * On (re)joining, the server sends its state and the client pushes what the server is missing, so changes
 * made while offline are not lost.
 */
export function connectWhiteboard(whiteboardId: string, doc: Y.Doc, handlers: SyncHandlers): WhiteboardSync {
  const devUser = devIdentityEnabled ? getDevUser() : null
  const url = devUser ? `${hubPath}?devUser=${encodeURIComponent(devUser)}` : hubPath
  const connection = new HubConnectionBuilder().withUrl(url, hubConnectionOptions).withAutomaticReconnect().configureLogging(LogLevel.Warning).build()

  let stopped = false
  let joined = false
  let canEdit = false
  let pending: Uint8Array[] = []
  let timer: ReturnType<typeof setTimeout> | null = null
  const announced = new Set<string>()
  let lastPresence: [number | null, number | null, string | null] = [null, null, null]

  const flush = async () => {
    timer = null
    if (!joined || !canEdit || pending.length === 0 || connection.state !== HubConnectionState.Connected) return
    const update = Y.mergeUpdates(pending)
    pending = []
    try {
      await connection.invoke('PushUpdate', whiteboardId, toBase64(update))
    } catch (e) {
      // Not stored. Yjs cannot take a change back, so the caller starts over with a fresh document.
      handlers.onRejected((e as Error).message)
    }
  }

  const schedule = () => {
    if (timer === null) timer = setTimeout(() => void flush(), pushDelay)
  }

  const onUpdate = (update: Uint8Array, origin: unknown) => {
    if (origin === remoteOrigin) return
    pending.push(update)
    schedule()
  }
  doc.on('update', onUpdate)

  connection.on('Update', (message: { whiteboardId: string; update: string }) => {
    if (message.whiteboardId === whiteboardId) Y.applyUpdate(doc, fromBase64(message.update), remoteOrigin)
  })
  connection.on('Presence', (message: Peer & { whiteboardId: string }) => {
    if (message.whiteboardId !== whiteboardId) return
    handlers.onPeer(message)
    // Someone new: tell them we are here too.
    if (!announced.has(message.connectionId)) {
      announced.add(message.connectionId)
      sendPresence(...lastPresence)
    }
  })
  connection.on('PresenceLeft', (message: { whiteboardId: string; connectionId: string }) => {
    if (message.whiteboardId !== whiteboardId) return
    announced.delete(message.connectionId)
    handlers.onPeerLeft(message.connectionId)
  })

  async function join() {
    joined = false
    const response = await connection.invoke<{ state: string; canEdit: boolean }>('Join', whiteboardId)
    const state = fromBase64(response.state)
    canEdit = response.canEdit
    // What this client has that the server does not (offline edits) goes up; the rest comes down.
    const missing = Y.encodeStateAsUpdate(doc, Y.encodeStateVectorFromUpdate(state))
    Y.applyUpdate(doc, state, remoteOrigin)
    joined = true
    pending = canEdit && missing.length > 2 ? [missing, ...pending] : []
    handlers.onStatus('online', canEdit)
    sendPresence(...lastPresence)
    schedule()
  }

  function sendPresence(x: number | null, y: number | null, selectedObjectId: string | null) {
    lastPresence = [x, y, selectedObjectId]
    if (!joined || connection.state !== HubConnectionState.Connected) return
    connection.invoke('UpdatePresence', whiteboardId, { x, y, selectedObjectId }).catch(() => {
      // Presence is best effort.
    })
  }

  connection.onreconnecting(() => {
    joined = false
    handlers.onStatus('offline', canEdit)
  })
  connection.onreconnected(() => {
    announced.clear()
    void join().catch((e: Error) => handlers.onRejected(e.message))
  })
  connection.onclose(() => {
    if (!stopped) handlers.onStatus('offline', canEdit)
  })

  handlers.onStatus('connecting', false)
  const started = connection
    .start()
    .then(() => (stopped ? undefined : join()))
    .catch((e: Error) => {
      if (!stopped) {
        handlers.onStatus('offline', false)
        handlers.onRejected(e.message)
      }
    })

  return {
    sendPresence,
    stop: () => {
      stopped = true
      doc.off('update', onUpdate)
      if (timer !== null) clearTimeout(timer)
      void started.finally(async () => {
        await flush().catch(() => {})
        await connection.stop()
      })
    },
  }
}
