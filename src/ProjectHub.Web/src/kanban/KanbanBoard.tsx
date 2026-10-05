import { useCallback, useEffect, useRef, useState, type DragEvent, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { initials } from '../identity/initials'
import { createTask, taskStatuses, type TaskStatus } from '../tasks/api'
import { DueDate } from '../tasks/DueDate'
import { celebrate } from '../ui/confetti'
import { PriorityBadge } from '../tasks/PriorityBadge'
import { NewTaskForm } from '../tasks/TaskBoard'
import { TaskDetails } from '../tasks/TaskDetails'
import {
  createColumn,
  deleteColumn,
  fetchBoard,
  moveCard,
  moveColumn,
  updateColumn,
  type KanbanBoard as Board,
  type KanbanCard,
  type KanbanColumn,
} from './api'
import { useLatest } from '../api/useLatest'

type Props = { project: ProjectDetails; me: Me; revision: number; onChanged: () => void; initialTaskId?: string | null }

type DropTarget = { columnId: string; index: number }

const conflictText = 'Jemand anderes hat das Board inzwischen geändert. Der aktuelle Stand wurde geladen.'

/** Removes the card from its column and inserts it at the target, as the server will. */
function withCardMoved(board: Board, card: KanbanCard, target: DropTarget): Board {
  const columns = board.columns.map((column) => ({ ...column, cards: column.cards.filter((c) => c.id !== card.id) }))
  const destination = columns.find((column) => column.id === target.columnId)
  if (destination) {
    destination.cards.splice(Math.min(target.index, destination.cards.length), 0, {
      ...card,
      status: destination.taskStatus,
    })
  }
  return { ...board, columns }
}

export function KanbanBoard({ project, me, revision, onChanged, initialTaskId = null }: Props) {
  const [board, setBoard] = useState<Board | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [dragging, setDragging] = useState<KanbanCard | null>(null)
  // Where the last drop or click happened, so the confetti starts at the card.
  const lastPointer = useRef({ x: window.innerWidth / 2, y: window.innerHeight / 3 })
  const [dropTarget, setDropTarget] = useState<DropTarget | null>(null)
  const [selected, setSelected] = useState<string | null>(initialTaskId)
  const { canContribute, canEdit } = project.capabilities

  const latest = useLatest()
  const load = useCallback(() => {
    latest(fetchBoard(project.id)).then(setBoard, (e: Error) => setError(e.message))
  }, [latest, project.id])

  useEffect(load, [load, revision])

  /** Runs a change that answers with the new board; a conflict reloads the current state. */
  async function apply(action: () => Promise<Board | void>) {
    setError(null)
    try {
      const updated = await action()
      if (updated) setBoard(updated)
      else load()
      onChanged()
    } catch (e) {
      setError(e instanceof ApiError && e.status === 409 ? conflictMessage(e) : (e as Error).message)
      load()
    }
  }

  function move(card: KanbanCard, target: DropTarget) {
    if (!board) return
    const source = board.columns.find((column) => column.cards.some((c) => c.id === card.id))
    // The index counts the cards without the moved one, like the server does.
    const from = source?.cards.findIndex((c) => c.id === card.id) ?? -1
    const index = source?.id === target.columnId && from >= 0 && from < target.index ? target.index - 1 : target.index
    if (source?.id === target.columnId && from === index) return
    setBoard(withCardMoved(board, card, { ...target, index }))
    const targetColumn = board.columns.find((column) => column.id === target.columnId)
    if (targetColumn?.taskStatus === 'done' && source?.taskStatus !== 'done') celebrate(lastPointer.current.x, lastPointer.current.y)
    void apply(() => moveCard(card, target.columnId, index))
  }

  function drop(event: DragEvent) {
    event.preventDefault()
    if (dragging && dropTarget) move(dragging, dropTarget)
    setDragging(null)
    setDropTarget(null)
  }

  function overCard(event: DragEvent, columnId: string, index: number) {
    if (!dragging) return
    event.preventDefault()
    event.stopPropagation()
    const box = event.currentTarget.getBoundingClientRect()
    const after = event.clientY > box.top + box.height / 2
    setDropTarget({ columnId, index: after ? index + 1 : index })
  }

  function overColumn(event: DragEvent, column: KanbanColumn) {
    if (!dragging) return
    event.preventDefault()
    if (dropTarget?.columnId !== column.id) setDropTarget({ columnId: column.id, index: column.cards.length })
  }

  if (!board) return error ? <p role="alert">{error}</p> : <p>Board wird geladen …</p>

  return (
    <section className="panel kanban" aria-labelledby="board-heading">
      <h3 id="board-heading">Board</h3>
      {error && <p role="alert">{error}</p>}
      {canContribute && (
        <NewTaskForm
          label="Neue Aufgabe"
          onCreate={async (title) => {
            await createTask(project.id, title, null)
            load()
            onChanged()
          }}
        />
      )}
      <div
        className="kanban-columns"
        onPointerDownCapture={(event) => (lastPointer.current = { x: event.clientX, y: event.clientY })}
        onDragOverCapture={(event) => (lastPointer.current = { x: event.clientX, y: event.clientY })}
      >
        {board.columns.map((column, columnIndex) => {
          const overLimit = column.wipLimit !== null && column.cards.length > column.wipLimit
          return (
            <section
              key={column.id}
              className={`kanban-column${dropTarget?.columnId === column.id ? ' drop-active' : ''}`}
              aria-label={column.name}
              onDragOver={(event) => overColumn(event, column)}
              onDrop={drop}
            >
              <header className="kanban-column-header">
                <h4>
                  <span className={`status-dot status-${column.taskStatus}`} aria-hidden="true" />
                  {column.name}
                </h4>
                <span className={overLimit ? 'wip over' : 'wip'} title="Karten / WIP-Limit">
                  {column.cards.length}
                  {column.wipLimit !== null && ` / ${column.wipLimit}`}
                </span>
              </header>
              {column.name !== taskStatuses[column.taskStatus] && (
                <p className="muted kanban-status">Status: {taskStatuses[column.taskStatus]}</p>
              )}
              {canEdit && (
                <ColumnSettings
                  key={`${column.id}-${column.version}`}
                  column={column}
                  isFirst={columnIndex === 0}
                  isLast={columnIndex === board.columns.length - 1}
                  onSave={(changes) => apply(() => updateColumn(column, changes))}
                  onMove={(offset) => apply(() => moveColumn(column, columnIndex + offset))}
                  onDelete={() => {
                    if (window.confirm(`Spalte „${column.name}“ löschen? Die Karten bleiben erhalten.`)) {
                      void apply(() => deleteColumn(column))
                    }
                  }}
                />
              )}
              <ol className="kanban-cards">
                {column.cards.map((card, index) => (
                  <li
                    key={card.id}
                    className={[
                      'kanban-card',
                      `priority-${card.priority}`,
                      card.id === selected ? 'selected' : '',
                      card.id === dragging?.id ? 'dragging' : '',
                      dropTarget?.columnId === column.id && dropTarget.index === index ? 'drop-before' : '',
                    ]
                      .filter(Boolean)
                      .join(' ')}
                    draggable={canContribute}
                    onDragStart={(event) => {
                      event.dataTransfer.effectAllowed = 'move'
                      event.dataTransfer.setData('text/plain', card.id)
                      setDragging(card)
                    }}
                    onDragEnd={() => {
                      setDragging(null)
                      setDropTarget(null)
                    }}
                    onDragOver={(event) => overCard(event, column.id, index)}
                  >
                    <button
                      type="button"
                      className="kanban-card-title"
                      onClick={() => setSelected(card.id === selected ? null : card.id)}
                    >
                      {card.title}
                    </button>
                    <div className="kanban-card-meta">
                      <PriorityBadge priority={card.priority} />
                      {card.dueDate && <DueDate date={card.dueDate} done={card.status === 'done'} />}
                      {card.subtaskCount > 0 && (
                        <span className="task-chip">
                          <span aria-hidden="true">🧩</span> {card.subtaskCount} Unteraufgaben
                        </span>
                      )}
                    </div>
                    {card.progress > 0 && (
                      <div
                        className="kanban-progress"
                        role="progressbar"
                        aria-label={`Fortschritt von „${card.title}“`}
                        aria-valuenow={card.progress}
                        aria-valuemin={0}
                        aria-valuemax={100}
                      >
                        <span style={{ width: `${card.progress}%` }} />
                      </div>
                    )}
                    {card.assigneeName && (
                      <span className="kanban-assignee">
                        <span className="avatar small" aria-hidden="true">
                          {initials(card.assigneeName)}
                        </span>
                        {card.assigneeName}
                      </span>
                    )}
                    {canContribute && (
                      <select
                        aria-label={`„${card.title}“ verschieben nach`}
                        className="kanban-move"
                        value={column.id}
                        onChange={(event) =>
                          move(card, {
                            columnId: event.target.value,
                            index: board.columns.find((c) => c.id === event.target.value)?.cards.length ?? 0,
                          })
                        }
                      >
                        {board.columns.map((option) => (
                          <option key={option.id} value={option.id}>
                            {option.name}
                          </option>
                        ))}
                      </select>
                    )}
                  </li>
                ))}
                {dropTarget?.columnId === column.id && dropTarget.index >= column.cards.length && (
                  <li className="drop-placeholder" aria-hidden="true" />
                )}
              </ol>
              {column.cards.length === 0 && dropTarget?.columnId !== column.id && <p className="kanban-empty">Noch keine Karten. Zieh eine hierher ✨</p>}
            </section>
          )
        })}
        {canEdit && <NewColumnForm onCreate={(name, status) => apply(() => createColumn(project.id, name, status))} />}
      </div>
      {selected && (
        <TaskDetails
          key={selected}
          taskId={selected}
          project={project}
          me={me}
          onChanged={() => {
            load()
            onChanged()
          }}
          onDeleted={() => {
            setSelected(null)
            load()
            onChanged()
          }}
        />
      )}
    </section>
  )
}

function conflictMessage(error: ApiError): string {
  return error.message.includes('at least one column') ? 'Jeder Status braucht mindestens eine Spalte.' : conflictText
}

type SettingsProps = {
  column: KanbanColumn
  isFirst: boolean
  isLast: boolean
  onSave: (changes: { name?: string; taskStatus?: TaskStatus; wipLimit?: number | null }) => void
  onMove: (offset: number) => void
  onDelete: () => void
}

function ColumnSettings({ column, isFirst, isLast, onSave, onMove, onDelete }: SettingsProps) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(column.name)
  const [status, setStatus] = useState<TaskStatus>(column.taskStatus)
  const [wipLimit, setWipLimit] = useState(column.wipLimit?.toString() ?? '')

  function submit(event: FormEvent) {
    event.preventDefault()
    const limit = wipLimit === '' ? null : Number(wipLimit)
    const changes = {
      ...(name !== column.name && { name }),
      ...(status !== column.taskStatus && { taskStatus: status }),
      ...(limit !== column.wipLimit && { wipLimit: limit }),
    }
    if (Object.keys(changes).length > 0) onSave(changes)
    setOpen(false)
  }

  return (
    <div className="column-settings">
      <button
        type="button"
        className="link-button"
        aria-expanded={open}
        aria-label={`Spalte „${column.name}“ bearbeiten`}
        onClick={() => setOpen(!open)}
      >
        Bearbeiten
      </button>
      {open && (
        <form className="stacked-form" onSubmit={submit}>
          <label>
            Spaltenname
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={100} />
          </label>
          <label>
            Status der Karten
            <select value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>
              {Object.entries(taskStatuses).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label>
            WIP-Limit
            <input type="number" min={1} value={wipLimit} onChange={(event) => setWipLimit(event.target.value)} />
          </label>
          <div className="row">
            <button type="button" aria-label="Spalte nach links" disabled={isFirst} onClick={() => onMove(-1)}>
              ←
            </button>
            <button type="button" aria-label="Spalte nach rechts" disabled={isLast} onClick={() => onMove(1)}>
              →
            </button>
            <button type="submit">Spalte speichern</button>
          </div>
          <button type="button" className="danger" onClick={onDelete}>
            Spalte löschen
          </button>
        </form>
      )}
    </div>
  )
}

function NewColumnForm({ onCreate }: { onCreate: (name: string, status: TaskStatus) => void }) {
  const [name, setName] = useState('')
  const [status, setStatus] = useState<TaskStatus>('in_progress')

  function submit(event: FormEvent) {
    event.preventDefault()
    onCreate(name, status)
    setName('')
  }

  return (
    <form className="kanban-column new-column stacked-form" onSubmit={submit}>
      <label>
        Neue Spalte
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={100} />
      </label>
      <label>
        Status der Karten
        <select value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>
          {Object.entries(taskStatuses).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <button type="submit">Spalte anlegen</button>
    </form>
  )
}
