import { Reveal } from '../ui/Reveal'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { createWhiteboard, deleteWhiteboard, fetchWhiteboards, renameWhiteboard, type Whiteboard } from './api'
import { BoardEditor } from './BoardEditor'
import { useLatest } from '../api/useLatest'
import { EmptyState } from '../ui/EmptyState'

type Props = { project: ProjectDetails; me: Me; revision: number; onChanged: () => void }

/** The whiteboards of a project: list, create, rename, delete, and the selected board. */
export function WhiteboardPanel({ project, me, revision, onChanged }: Props) {
  const [boards, setBoards] = useState<Whiteboard[] | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const { canEdit } = project.capabilities

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchWhiteboards(project.id)).then(
      (page) => {
        setBoards(page.items)
        setSelectedId((current) => (current && page.items.some((b) => b.id === current) ? current : (page.items[0]?.id ?? null)))
      },
      (e: Error) => setError(e.message),
    )
  }, [latest, project.id])

  // Reloads when anyone changes the project, so renamed or deleted boards show up for everyone.
  useEffect(load, [load, revision])

  async function run(action: () => Promise<void>) {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(e instanceof ApiError && e.status === 409 ? 'Jemand anderes hat das Whiteboard inzwischen geändert. Der aktuelle Stand wurde geladen.' : (e as Error).message)
    }
    load()
    onChanged()
  }

  async function create(event: FormEvent) {
    event.preventDefault()
    await run(async () => {
      const board = await createWhiteboard(project.id, name)
      setName('')
      setSelectedId(board.id)
    })
  }

  const selected = boards?.find((b) => b.id === selectedId) ?? null

  return (
    <section className="whiteboards" aria-labelledby="whiteboards-heading">
      <h3 id="whiteboards-heading" className="visually-hidden">
        Whiteboards
      </h3>
      <div className="whiteboard-bar">
        {boards && boards.length > 0 && (
          <label>
            Whiteboard
            <select value={selectedId ?? ''} onChange={(event) => setSelectedId(event.target.value)}>
              {boards.map((board) => (
                <option key={board.id} value={board.id}>
                  {board.name}
                </option>
              ))}
            </select>
          </label>
        )}
        {canEdit && (
          <Reveal label="Whiteboard">
            {(close) => (
              <form className="quick-create" onSubmit={create}>
                <input
                  aria-label="Neues Whiteboard"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  onBlur={() => {
                    if (!name.trim()) close()
                  }}
                  required
                  maxLength={200}
                  placeholder="Name, Enter zum Anlegen"
                  autoFocus
                />
              </form>
            )}
          </Reveal>
        )}
      </div>
      {error && <p role="alert">{error}</p>}
      {boards === null ? (
        <p className="muted">Whiteboards werden geladen …</p>
      ) : !selected ? (
        <EmptyState emoji="🎨">{canEdit ? 'Noch kein Whiteboard. Lege mit „+ Whiteboard“ das erste an.' : 'Dieses Projekt hat noch kein Whiteboard.'}</EmptyState>
      ) : (
        <BoardEditor
          key={selected.id}
          board={selected}
          project={project}
          me={me}
          revision={revision}
          onChanged={onChanged}
          onRename={(newName) => run(async () => void (await renameWhiteboard(selected, newName)))}
          onDelete={() => {
            if (window.confirm(`Whiteboard „${selected.name}“ wirklich löschen?`)) void run(() => deleteWhiteboard(selected.id))
          }}
        />
      )}
    </section>
  )
}
