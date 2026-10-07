import { createColumn, fetchBoard, moveColumn, updateColumn, type KanbanBoard } from '../kanban/api'
import { createTask, type TaskStatus } from '../tasks/api'

export type ProjectTemplate = {
  id: 'empty' | 'kanban' | 'event' | 'software'
  name: string
  description: string
  columns: { name: string; status: TaskStatus }[]
  /** First tasks, in the first column. */
  tasks: string[]
}

/**
 * Starting points for a new project (Tesler's law: the app carries the setup, not the person). Applied in the
 * browser with the normal board and task endpoints, so a template is just a quicker way to do what one could click.
 */
export const projectTemplates: ProjectTemplate[] = [
  {
    id: 'empty',
    name: 'Leer',
    description: 'Offen, In Arbeit, Erledigt',
    columns: [
      { name: 'Offen', status: 'todo' },
      { name: 'In Arbeit', status: 'in_progress' },
      { name: 'Erledigt', status: 'done' },
    ],
    tasks: [],
  },
  {
    id: 'kanban',
    name: 'Kanban',
    description: 'Backlog, In Arbeit, Review, Erledigt',
    columns: [
      { name: 'Backlog', status: 'todo' },
      { name: 'In Arbeit', status: 'in_progress' },
      { name: 'Review', status: 'in_progress' },
      { name: 'Erledigt', status: 'done' },
    ],
    tasks: [],
  },
  {
    id: 'event',
    name: 'Veranstaltung',
    description: 'Spalten für die Planung und sechs typische Aufgaben',
    columns: [
      { name: 'Offen', status: 'todo' },
      { name: 'In Arbeit', status: 'in_progress' },
      { name: 'Warten auf Rückmeldung', status: 'in_progress' },
      { name: 'Erledigt', status: 'done' },
    ],
    tasks: ['Termin und Ort festlegen', 'Budget planen', 'Einladungen verschicken', 'Catering klären', 'Technik und Material organisieren', 'Nachbereitung und Dank'],
  },
  {
    id: 'software',
    name: 'Software',
    description: 'Backlog, Bereit, In Arbeit, Review, Test, Erledigt',
    columns: [
      { name: 'Backlog', status: 'todo' },
      { name: 'Bereit', status: 'todo' },
      { name: 'In Arbeit', status: 'in_progress' },
      { name: 'Review', status: 'in_progress' },
      { name: 'Test', status: 'in_progress' },
      { name: 'Erledigt', status: 'done' },
    ],
    tasks: ['Anforderungen sammeln', 'Architektur festlegen', 'Testumgebung einrichten'],
  },
]

/** Turns the default board of a new project into the template's columns and adds its first tasks. */
export async function applyTemplate(projectId: string, template: ProjectTemplate) {
  if (template.id === 'empty') return
  let board: KanbanBoard = await fetchBoard(projectId)
  const used = new Set<string>()
  const ids: string[] = []

  for (const target of template.columns) {
    const existing = board.columns.find((column) => column.taskStatus === target.status && !used.has(column.id))
    if (existing) {
      used.add(existing.id)
      ids.push(existing.id)
      if (existing.name !== target.name) board = await updateColumn(existing, { name: target.name })
    } else {
      const before = new Set(board.columns.map((column) => column.id))
      board = await createColumn(projectId, target.name, target.status)
      const created = board.columns.find((column) => !before.has(column.id))
      if (!created) throw new Error(`Spalte „${target.name}“ wurde nicht angelegt.`)
      used.add(created.id)
      ids.push(created.id)
    }
  }

  for (const [index, id] of ids.entries()) {
    const position = board.columns.findIndex((column) => column.id === id)
    if (position !== index) board = await moveColumn(board.columns[position], index)
  }

  for (const title of template.tasks) await createTask(projectId, title, null, template.columns[0].status)
}
