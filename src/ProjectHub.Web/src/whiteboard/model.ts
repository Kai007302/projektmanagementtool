import * as Y from 'yjs'

/**
 * The whiteboard document (ADR 0009): a root map `objects` with one Y.Map per object. Fields are plain
 * values, so concurrent changes to different fields or objects merge; the same field is last-writer-wins.
 * Task cards hold only `taskId`; their data is loaded live from the API.
 */
export const objectsKey = 'objects'

export type ObjectType = 'sticky' | 'rect' | 'ellipse' | 'text' | 'arrow' | 'task'

export type Color = 'yellow' | 'green' | 'blue' | 'pink' | 'gray' | 'white'

export type BoardObject = {
  id: string
  type: ObjectType
  x: number
  y: number
  w: number
  h: number
  /** End point of arrows. */
  x2: number
  y2: number
  color: Color
  text: string
  taskId: string | null
}

export const objectTypes: Record<ObjectType, string> = {
  sticky: 'Notiz',
  rect: 'Rechteck',
  ellipse: 'Ellipse',
  text: 'Text',
  arrow: 'Pfeil',
  task: 'Aufgabe',
}

export const colors: Record<Color, string> = {
  yellow: 'Gelb',
  green: 'Grün',
  blue: 'Blau',
  pink: 'Rosa',
  gray: 'Grau',
  white: 'Weiß',
}

const defaults: Record<ObjectType, { w: number; h: number; color: Color; text: string }> = {
  sticky: { w: 180, h: 120, color: 'yellow', text: 'Neue Notiz' },
  rect: { w: 200, h: 120, color: 'blue', text: '' },
  ellipse: { w: 160, h: 110, color: 'green', text: '' },
  text: { w: 220, h: 40, color: 'white', text: 'Text' },
  arrow: { w: 0, h: 0, color: 'gray', text: '' },
  task: { w: 220, h: 90, color: 'white', text: '' },
}

export const minSize = 20

/** Origin of changes made in this browser; the undo manager only tracks these. */
export const localOrigin = Symbol('local')

export const objectsOf = (doc: Y.Doc) => doc.getMap<Y.Map<unknown>>(objectsKey)

const numberOr = (value: unknown, fallback: number) => (typeof value === 'number' && Number.isFinite(value) ? value : fallback)

const stringOr = (value: unknown, fallback: string) => (typeof value === 'string' ? value : fallback)

/** Reads all objects that have a known type. Values from other clients are checked, never trusted. */
export function readObjects(doc: Y.Doc): BoardObject[] {
  const result: BoardObject[] = []
  objectsOf(doc).forEach((value, id) => {
    if (!(value instanceof Y.Map)) return
    const type = value.get('type')
    if (typeof type !== 'string' || !(type in objectTypes)) return
    const base = defaults[type as ObjectType]
    const x = numberOr(value.get('x'), 0)
    const y = numberOr(value.get('y'), 0)
    const color = stringOr(value.get('color'), base.color)
    result.push({
      id,
      type: type as ObjectType,
      x,
      y,
      w: Math.max(minSize, numberOr(value.get('w'), base.w)),
      h: Math.max(minSize, numberOr(value.get('h'), base.h)),
      x2: numberOr(value.get('x2'), x + 120),
      y2: numberOr(value.get('y2'), y),
      color: color in colors ? (color as Color) : base.color,
      text: stringOr(value.get('text'), ''),
      taskId: typeof value.get('taskId') === 'string' ? (value.get('taskId') as string) : null,
    })
  })
  // Stable order for rendering and the object list: top to bottom, left to right.
  return result.sort((a, b) => a.y - b.y || a.x - b.x || a.id.localeCompare(b.id))
}

export const newObjectId = () => (globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`).replaceAll('-', '')

/** Adds an object centered on the given point and returns its id. */
export function addObject(doc: Y.Doc, type: ObjectType, center: { x: number; y: number }, taskId?: string): string {
  const id = newObjectId()
  const base = defaults[type]
  doc.transact(() => {
    const item = new Y.Map<unknown>()
    item.set('type', type)
    if (type === 'arrow') {
      item.set('x', Math.round(center.x - 60))
      item.set('y', Math.round(center.y))
      item.set('x2', Math.round(center.x + 60))
      item.set('y2', Math.round(center.y))
    } else {
      item.set('x', Math.round(center.x - base.w / 2))
      item.set('y', Math.round(center.y - base.h / 2))
      item.set('w', base.w)
      item.set('h', base.h)
    }
    item.set('color', base.color)
    if (base.text) item.set('text', base.text)
    if (taskId) item.set('taskId', taskId)
    objectsOf(doc).set(id, item)
  }, localOrigin)
  return id
}

export type Changes = Partial<Pick<BoardObject, 'x' | 'y' | 'w' | 'h' | 'x2' | 'y2' | 'color' | 'text'>>

/** Changes only the given fields, so others editing other fields of the same object are not overwritten. */
export function updateObject(doc: Y.Doc, id: string, changes: Changes) {
  const item = objectsOf(doc).get(id)
  if (!(item instanceof Y.Map)) return
  doc.transact(() => {
    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined) continue
      if (typeof value === 'number') {
        const rounded = Math.round(value)
        item.set(key, key === 'w' || key === 'h' ? Math.max(minSize, rounded) : rounded)
      } else if (item.get(key) !== value) {
        item.set(key, value)
      }
    }
  }, localOrigin)
}

/** Moves an object by a delta; arrows move both ends. */
export function moveBy(object: BoardObject, dx: number, dy: number): Changes {
  return object.type === 'arrow'
    ? { x: object.x + dx, y: object.y + dy, x2: object.x2 + dx, y2: object.y2 + dy }
    : { x: object.x + dx, y: object.y + dy }
}

export function removeObject(doc: Y.Doc, id: string) {
  doc.transact(() => objectsOf(doc).delete(id), localOrigin)
}

/** A short description for the object list and screen readers. */
export function describe(object: BoardObject, taskTitle?: string | null): string {
  const kind = objectTypes[object.type]
  if (object.type === 'task') return `${kind}: ${taskTitle ?? 'nicht verfügbar'}`
  if (object.type === 'arrow') return `${kind} von (${Math.round(object.x)}, ${Math.round(object.y)}) nach (${Math.round(object.x2)}, ${Math.round(object.y2)})`
  return object.text.trim() ? `${kind}: ${object.text.trim()}` : `${kind} (${colors[object.color]})`
}
