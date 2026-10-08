import { useCallback, useEffect, useRef, useState, type CSSProperties, type DragEvent, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { Me } from '../identity/api'
import type { ProjectDetails } from '../projects/api'
import { initials } from '../identity/initials'
import { assigneeText, createTask, taskStatuses, type NewTaskDetails, type TaskStatus } from '../tasks/api'
import { DueDate } from '../tasks/DueDate'
import { celebrate } from '../ui/confetti'
import { PriorityBadge } from '../tasks/PriorityBadge'
import { NewTaskDialog } from '../tasks/NewTaskDialog'
import { TaskDetails } from '../tasks/TaskDetails'
import {
  createColumn,
  deleteColumn,
  fetchBoard,
  moveCard,
  moveColumn,
  columnColors,
  updateColumn,
  type ColumnColor,
  type KanbanBoard as Board,
  type KanbanCard,
  type KanbanColumn,
} from './api'
import { useLatest } from '../api/useLatest'
import { InlineEdit } from '../ui/InlineEdit'
import { Menu } from '../ui/Menu'
import { Reveal } from '../ui/Reveal'
import { Skeleton } from '../ui/Skeleton'

type Props = {
  project: ProjectDetails
  me: Me
  revision: number
  onChanged: () => void
  initialTaskId?: string | null
}

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
  const [colorOf, setColorOf] = useState<string | null>(null)
  const [creatingIn, setCreatingIn] = useState<KanbanColumn | null>(null)
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

  /** A new card goes to the end of the column it was added in, like in Trello; another status puts it in that status's first column. */
  async function createIn(column: KanbanColumn, title: string, status: TaskStatus, details: NewTaskDetails) {
    const task = await createTask(project.id, title, null, status, details)
    if (status !== column.taskStatus) {
      setBoard(await fetchBoard(project.id))
      onChanged()
      return
    }
    const fresh = await fetchBoard(project.id)
    const card = fresh.columns.flatMap((c) => c.cards).find((c) => c.id === task.id)
    const target = fresh.columns.find((c) => c.id === column.id)
    setBoard(fresh)
    if (card && target && !target.cards.some((c) => c.id === card.id)) {
      setBoard(await moveCard(card, column.id, target.cards.length))
    }
    onChanged()
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

  if (!board) return error ? <p role="alert">{error}</p> : <Skeleton kind="board" label="Board wird geladen" />

  return (
    <section className="panel kanban" aria-labelledby="board-heading">
      <h3 id="board-heading">Board</h3>
      {error && <p role="alert">{error}</p>}
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
              className={`kanban-column${column.color ? ' has-color' : ''}${dropTarget?.columnId === column.id ? ' drop-active' : ''}`}
              style={column.color ? ({ '--column-color': columnColors[column.color].value } as CSSProperties) : undefined}
              aria-label={column.name}
              onDragOver={(event) => overColumn(event, column)}
              onDrop={drop}
            >
              <header className="kanban-column-header">
                <h4>
                  <span className={`status-dot status-${column.taskStatus}`} aria-hidden="true" />
                  <InlineEdit
                    key={`${column.id}-${column.version}`}
                    value={column.name}
                    label={`Spaltenname „${column.name}“`}
                    maxLength={100}
                    editable={canEdit}
                    onSave={(name) => apply(() => updateColumn(column, { name }))}
                  />
                </h4>
                <span className={overLimit ? 'wip over' : 'wip'} title="Karten / WIP-Limit">
                  {column.cards.length}
                  {column.wipLimit !== null && ` / ${column.wipLimit}`}
                </span>
                {canEdit && (
                  <Menu
                    label={`Spalte „${column.name}“`}
                    items={[
                      { label: 'Farbe', onSelect: () => setColorOf(column.id) },
                      { label: 'Nach links', disabled: columnIndex === 0, onSelect: () => void apply(() => moveColumn(column, columnIndex - 1)) },
                      { label: 'Nach rechts', disabled: columnIndex === board.columns.length - 1, onSelect: () => void apply(() => moveColumn(column, columnIndex + 1)) },
                      {
                        label: 'Spalte löschen',
                        danger: true,
                        onSelect: () => {
                          if (window.confirm(`Spalte „${column.name}“ löschen? Die Karten bleiben erhalten.`)) void apply(() => deleteColumn(column))
                        },
                      },
                    ]}
                  />
                )}
              </header>
              {column.name !== taskStatuses[column.taskStatus] && (
                <p className="muted kanban-status">Status: {taskStatuses[column.taskStatus]}</p>
              )}
              {canEdit && colorOf === column.id && (
                <ColumnColorPicker
                  column={column}
                  onPick={(color) => {
                    setColorOf(null)
                    if (color !== column.color) void apply(() => updateColumn(column, { color }))
                  }}
                  onClose={() => setColorOf(null)}
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
                    <div className="kanban-card-top">
                      <button
                        type="button"
                        className="kanban-card-title"
                        onClick={() => setSelected(card.id === selected ? null : card.id)}
                      >
                        {card.title}
                      </button>
                      {canContribute && (
                        <Menu
                          className="kanban-card-menu"
                          label={`Aktionen für „${card.title}“`}
                          items={board.columns
                            .filter((option) => option.id !== column.id)
                            .map((option) => ({
                              label: `Nach „${option.name}“ verschieben`,
                              onSelect: () => move(card, { columnId: option.id, index: option.cards.length }),
                            }))}
                        />
                      )}
                    </div>
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
                    {card.assignees.length > 0 && (
                      <span className="kanban-assignee">
                        <span className="avatar-stack" aria-hidden="true">
                          {card.assignees.slice(0, 3).map((assignee) => (
                            <span key={assignee.id} className="avatar small">
                              {initials(assignee.displayName)}
                            </span>
                          ))}
                        </span>
                        {assigneeText(card.assignees.map((assignee) => assignee.displayName))}
                      </span>
                    )}
                  </li>
                ))}
                {dropTarget?.columnId === column.id && dropTarget.index >= column.cards.length && (
                  <li className="drop-placeholder" aria-hidden="true" />
                )}
              </ol>
              {column.cards.length === 0 && dropTarget?.columnId !== column.id && <p className="kanban-empty">Noch keine Karten. Zieh eine hierher ✨</p>}
              {canContribute && (
                <button type="button" className="add-button" onClick={() => setCreatingIn(column)}>
                  + Aufgabe
                </button>
              )}
            </section>
          )
        })}
        {canEdit && (
          <div className="kanban-column new-column">
            <Reveal label="Spalte" iconOnly>{(close) => <NewColumnForm onCreate={(name) => apply(() => createColumn(project.id, name, 'in_progress'))} onClose={close} />}</Reveal>
          </div>
        )}
      </div>
      {creatingIn && (
        <NewTaskDialog
          project={project}
          status={creatingIn.taskStatus}
          where={creatingIn.name}
          onCreate={(title, status, details) => createIn(creatingIn, title, status, details)}
          onClose={() => setCreatingIn(null)}
        />
      )}
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
          onClose={() => setSelected(null)}
        />
      )}
    </section>
  )
}

function conflictMessage(error: ApiError): string {
  return error.message.includes('at least one column') ? 'Jeder Status braucht mindestens eine Spalte.' : conflictText
}

/** Name and colour are all a column offers; its status and WIP limit stay as they were created. */
function ColumnColorPicker({ column, onPick, onClose }: { column: KanbanColumn; onPick: (color: ColumnColor | null) => void; onClose: () => void }) {
  return (
    <div
      className="column-colors"
      role="group"
      aria-label={`Farbe der Spalte „${column.name}“`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <button type="button" className="swatch swatch-none" aria-pressed={column.color === null} title="Keine Farbe" aria-label="Keine Farbe" onClick={() => onPick(null)} autoFocus />
      {(Object.keys(columnColors) as ColumnColor[]).map((color) => (
        <button
          key={color}
          type="button"
          className="swatch"
          style={{ background: columnColors[color].value }}
          aria-pressed={column.color === color}
          title={columnColors[color].label}
          aria-label={columnColors[color].label}
          onClick={() => onPick(color)}
        />
      ))}
    </div>
  )
}

function NewColumnForm({ onCreate, onClose }: { onCreate: (name: string) => void; onClose: () => void }) {
  const [name, setName] = useState('')

  function submit(event: FormEvent) {
    event.preventDefault()
    onCreate(name)
    onClose()
  }

  return (
    <form className="quick-create" onSubmit={submit}>
      <input
        aria-label="Name der neuen Spalte"
        placeholder="Spaltenname, Enter zum Anlegen"
        value={name}
        onChange={(event) => setName(event.target.value)}
        onBlur={() => {
          if (!name.trim()) onClose()
        }}
        required
        maxLength={100}
        autoFocus
      />
    </form>
  )
}
