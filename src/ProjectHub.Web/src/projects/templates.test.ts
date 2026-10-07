import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { KanbanBoard, KanbanColumn } from '../kanban/api'
import { applyTemplate, projectTemplates } from './templates'

const api = vi.hoisted(() => ({ board: null as unknown as KanbanBoard, tasks: [] as string[] }))

vi.mock('../kanban/api', () => {
  const copy = () => ({ ...api.board, columns: [...api.board.columns] })
  return {
    fetchBoard: vi.fn(async () => copy()),
    updateColumn: vi.fn(async (column: KanbanColumn, changes: { name: string }) => {
      api.board.columns = api.board.columns.map((c) => (c.id === column.id ? { ...c, ...changes } : c))
      return copy()
    }),
    createColumn: vi.fn(async (_: string, name: string, taskStatus: KanbanColumn['taskStatus']) => {
      api.board.columns = [...api.board.columns, column(`c${api.board.columns.length + 1}`, name, taskStatus)]
      return copy()
    }),
    moveColumn: vi.fn(async (moved: KanbanColumn, index: number) => {
      const rest = api.board.columns.filter((c) => c.id !== moved.id)
      api.board.columns = [...rest.slice(0, index), moved, ...rest.slice(index)]
      return copy()
    }),
  }
})

vi.mock('../tasks/api', () => ({ createTask: vi.fn(async (_: string, title: string) => void api.tasks.push(title)) }))

function column(id: string, name: string, taskStatus: KanbanColumn['taskStatus']): KanbanColumn {
  return { id, name, taskStatus, wipLimit: null, version: 1, cards: [] }
}

const template = (id: string) => projectTemplates.find((t) => t.id === id)!

describe('applyTemplate', () => {
  beforeEach(() => {
    api.board = { id: 'b', projectId: 'p', name: 'Board', columns: [column('c1', 'Offen', 'todo'), column('c2', 'In Arbeit', 'in_progress'), column('c3', 'Erledigt', 'done')] }
    api.tasks = []
  })

  it.each(['kanban', 'event', 'software'])('builds the columns of the %s template in order', async (id) => {
    await applyTemplate('p', template(id))
    expect(api.board.columns.map((c) => [c.name, c.taskStatus])).toEqual(template(id).columns.map((c) => [c.name, c.status]))
    expect(api.tasks).toEqual(template(id).tasks)
  })

  it('leaves the default board of an empty project alone', async () => {
    await applyTemplate('p', template('empty'))
    expect(api.board.columns.map((c) => c.name)).toEqual(['Offen', 'In Arbeit', 'Erledigt'])
  })
})
