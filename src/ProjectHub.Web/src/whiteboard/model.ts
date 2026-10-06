import * as Y from 'yjs'

/**
 * The whiteboard document (ADR 0009): a root map `objects` with one Y.Map per object. Fields are plain
 * values, so concurrent changes to different fields or objects merge; the same field is last-writer-wins.
 * Task cards hold only `taskId`; their data is loaded live from the API. Arrows can hang on objects with
 * `from` and `to` (object ids), like connectors in draw.io; `locked` keeps an object in place.
 */
export const objectsKey = 'objects'

export type ObjectType = 'sticky' | 'rect' | 'ellipse' | 'diamond' | 'text' | 'arrow' | 'task'

export type Color =
  | 'white'
  | 'yellow'
  | 'orange'
  | 'red'
  | 'pink'
  | 'violet'
  | 'blue'
  | 'cyan'
  | 'teal'
  | 'green'
  | 'lime'
  | 'sand'
  | 'gray'
  | 'navy'
  | 'purple'
  | 'black'

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
  /** Arrows only: the objects the arrow starts and ends at; the end then follows the object. */
  from: string | null
  to: string | null
  /** Locked objects cannot be moved, resized, changed or removed until someone unlocks them. */
  locked: boolean
}

export const objectTypes: Record<ObjectType, string> = {
  sticky: 'Notiz',
  rect: 'Rechteck',
  ellipse: 'Ellipse',
  diamond: 'Raute',
  text: 'Text',
  arrow: 'Pfeil',
  task: 'Aufgabe',
}

/** The sixteen colors in palette order, like the sticky notes in Miro; the last three are dark with light text. */
export const colors: Record<Color, string> = {
  white: 'Weiß',
  yellow: 'Gelb',
  orange: 'Orange',
  red: 'Rot',
  pink: 'Rosa',
  violet: 'Violett',
  blue: 'Blau',
  cyan: 'Hellblau',
  teal: 'Türkis',
  green: 'Grün',
  lime: 'Hellgrün',
  sand: 'Sand',
  gray: 'Grau',
  navy: 'Dunkelblau',
  purple: 'Dunkelviolett',
  black: 'Schwarz',
}

export const isColor = (value: unknown): value is Color => typeof value === 'string' && Object.hasOwn(colors, value)

const defaults: Record<ObjectType, { w: number; h: number; color: Color; text: string }> = {
  sticky: { w: 180, h: 120, color: 'yellow', text: 'Neue Notiz' },
  rect: { w: 200, h: 120, color: 'blue', text: '' },
  ellipse: { w: 160, h: 110, color: 'green', text: '' },
  diamond: { w: 160, h: 120, color: 'yellow', text: '' },
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
      color: isColor(color) ? color : base.color,
      text: stringOr(value.get('text'), ''),
      taskId: typeof value.get('taskId') === 'string' ? (value.get('taskId') as string) : null,
      from: type === 'arrow' ? stringOr(value.get('from'), '') || null : null,
      to: type === 'arrow' ? stringOr(value.get('to'), '') || null : null,
      locked: value.get('locked') === true,
    })
  })
  const resolved = resolveConnectors(result)
  // Stable order for rendering and the object list: top to bottom, left to right.
  return resolved.sort((a, b) => a.y - b.y || a.x - b.x || a.id.localeCompare(b.id))
}

type Point = { x: number; y: number }

const centerOf = (object: BoardObject): Point => ({ x: object.x + object.w / 2, y: object.y + object.h / 2 })

/** Space between an object's outline and an arrow end, so the arrow head stays visible. */
const connectorGap = 3

/** Where the line from the object's center toward the point leaves the object's outline. */
export function outlinePoint(object: BoardObject, toward: Point): Point {
  const center = centerOf(object)
  const dx = toward.x - center.x
  const dy = toward.y - center.y
  if (dx === 0 && dy === 0) return center
  const rx = object.w / 2 + connectorGap
  const ry = object.h / 2 + connectorGap
  const t =
    object.type === 'ellipse'
      ? 1 / Math.sqrt((dx / rx) ** 2 + (dy / ry) ** 2)
      : object.type === 'diamond'
        ? 1 / (Math.abs(dx) / rx + Math.abs(dy) / ry)
        : Math.min(dx === 0 ? Infinity : rx / Math.abs(dx), dy === 0 ? Infinity : ry / Math.abs(dy))
  return { x: center.x + dx * t, y: center.y + dy * t }
}

/**
 * Puts the ends of attached arrows on the outlines of their objects, on the straight line between them. An end whose
 * object is gone (deleted by someone else at the same time) stays where it was last stored.
 */
function resolveConnectors(objects: BoardObject[]): BoardObject[] {
  const byId = new Map(objects.filter((o) => o.type !== 'arrow').map((o) => [o.id, o]))
  return objects.map((object) => {
    if (object.type !== 'arrow') return object
    const source = object.from ? byId.get(object.from) : undefined
    const target = object.to ? byId.get(object.to) : undefined
    if (!source && !target) return { ...object, from: null, to: null }
    const startAim = target ? centerOf(target) : { x: object.x2, y: object.y2 }
    const endAim = source ? centerOf(source) : { x: object.x, y: object.y }
    const start = source ? outlinePoint(source, startAim) : { x: object.x, y: object.y }
    const end = target ? outlinePoint(target, endAim) : { x: object.x2, y: object.y2 }
    return { ...object, x: start.x, y: start.y, x2: end.x, y2: end.y, from: source ? source.id : null, to: target ? target.id : null }
  })
}

/** A new arrow from one object to another object or to a free point, like dragging a connector in draw.io. */
export function addConnector(doc: Y.Doc, from: BoardObject, to: BoardObject | Point): string {
  const id = newObjectId()
  const target = 'id' in to ? to : null
  const aim = target ? centerOf(target) : (to as Point)
  const start = outlinePoint(from, aim)
  const end = target ? outlinePoint(target, centerOf(from)) : aim
  doc.transact(() => {
    const item = new Y.Map<unknown>()
    item.set('type', 'arrow')
    // The stored points are a fallback for clients that do not know `from` and `to`, or when an object is gone.
    item.set('x', Math.round(start.x))
    item.set('y', Math.round(start.y))
    item.set('x2', Math.round(end.x))
    item.set('y2', Math.round(end.y))
    item.set('color', defaults.arrow.color)
    item.set('from', from.id)
    if (target) item.set('to', target.id)
    objectsOf(doc).set(id, item)
  }, localOrigin)
  return id
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

/** An object without id, as templates describe them; missing fields take the type's defaults. */
export type NewObject = { type: Exclude<ObjectType, 'task'>; x: number; y: number; w?: number; h?: number; x2?: number; y2?: number; color?: Color; text?: string }

export type Bounds = { x: number; y: number; w: number; h: number }

/** The box around the given objects; arrows count with both ends. */
export function boundsOf(items: (Omit<NewObject, 'type'> & { type: ObjectType })[]): Bounds {
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (const item of items) {
    const base = defaults[item.type]
    const right = item.type === 'arrow' ? (item.x2 ?? item.x) : item.x + (item.w ?? base.w)
    const bottom = item.type === 'arrow' ? (item.y2 ?? item.y) : item.y + (item.h ?? base.h)
    minX = Math.min(minX, item.x, right)
    minY = Math.min(minY, item.y, bottom)
    maxX = Math.max(maxX, item.x, right)
    maxY = Math.max(maxY, item.y, bottom)
  }
  return items.length === 0 ? { x: 0, y: 0, w: 0, h: 0 } : { x: minX, y: minY, w: maxX - minX, h: maxY - minY }
}

/**
 * Adds several objects in one change (one undo step, one update for everyone), placed so their box is
 * centered on the given point. Returns the new ids and the box on the board.
 */
export function addObjects(doc: Y.Doc, items: NewObject[], center: { x: number; y: number }): { ids: string[]; bounds: Bounds } {
  const box = boundsOf(items)
  const dx = Math.round(center.x - box.x - box.w / 2)
  const dy = Math.round(center.y - box.y - box.h / 2)
  const ids: string[] = []
  doc.transact(() => {
    for (const object of items) {
      const base = defaults[object.type]
      const id = newObjectId()
      const item = new Y.Map<unknown>()
      item.set('type', object.type)
      item.set('x', Math.round(object.x + dx))
      item.set('y', Math.round(object.y + dy))
      if (object.type === 'arrow') {
        item.set('x2', Math.round((object.x2 ?? object.x + 120) + dx))
        item.set('y2', Math.round((object.y2 ?? object.y) + dy))
      } else {
        item.set('w', Math.max(minSize, Math.round(object.w ?? base.w)))
        item.set('h', Math.max(minSize, Math.round(object.h ?? base.h)))
      }
      item.set('color', object.color ?? base.color)
      const text = object.text ?? base.text
      if (text) item.set('text', text)
      objectsOf(doc).set(id, item)
      ids.push(id)
    }
  }, localOrigin)
  return { ids, bounds: { x: box.x + dx, y: box.y + dy, w: box.w, h: box.h } }
}

/** A new sticky note next to the given one (Miro's quick add): same size and color, empty, with a small gap. */
export function addNextTo(doc: Y.Doc, object: BoardObject, direction: 'right' | 'below'): string {
  const gap = 20
  const x = direction === 'right' ? object.x + object.w + gap : object.x
  const y = direction === 'below' ? object.y + object.h + gap : object.y
  const { ids } = addObjects(doc, [{ type: 'sticky', x, y, w: object.w, h: object.h, color: object.color, text: '' }], { x: x + object.w / 2, y: y + object.h / 2 })
  return ids[0]
}

/** Bulk mode: one sticky note per non-empty line, in a grid around the given point. At most 100 notes. */
export function stickyGrid(lines: string[], color: Color): NewObject[] {
  const texts = lines.map((line) => line.trim()).filter(Boolean).slice(0, 100)
  const columns = Math.ceil(Math.sqrt(texts.length))
  const { w, h } = defaults.sticky
  const gap = 20
  return texts.map((text, i) => ({ type: 'sticky', x: (i % columns) * (w + gap), y: Math.floor(i / columns) * (h + gap), w, h, color, text }))
}

export type Changes = Partial<Pick<BoardObject, 'x' | 'y' | 'w' | 'h' | 'x2' | 'y2' | 'color' | 'text' | 'from' | 'to' | 'locked'>>

/** Changes only the given fields, so others editing other fields of the same object are not overwritten. */
export function updateObject(doc: Y.Doc, id: string, changes: Changes) {
  const item = objectsOf(doc).get(id)
  if (!(item instanceof Y.Map)) return
  doc.transact(() => {
    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined) continue
      if (value === null || value === false) {
        // Detached ends and unlocked objects carry no field at all.
        if (item.has(key)) item.delete(key)
      } else if (typeof value === 'number') {
        const rounded = Math.round(value)
        item.set(key, key === 'w' || key === 'h' ? Math.max(minSize, rounded) : rounded)
      } else if (item.get(key) !== value) {
        item.set(key, value)
      }
    }
  }, localOrigin)
}

/** Moves an object by a delta; arrows move both ends, except ends that hang on an object. */
export function moveBy(object: BoardObject, dx: number, dy: number): Changes {
  if (object.type !== 'arrow') return { x: object.x + dx, y: object.y + dy }
  return {
    ...(!object.from && { x: object.x + dx, y: object.y + dy }),
    ...(!object.to && { x2: object.x2 + dx, y2: object.y2 + dy }),
  }
}

/** Removes the object and the arrows hanging on it, in one change. */
export function removeObject(doc: Y.Doc, id: string) {
  const objects = objectsOf(doc)
  doc.transact(() => {
    objects.delete(id)
    objects.forEach((value, key) => {
      if (value instanceof Y.Map && value.get('type') === 'arrow' && (value.get('from') === id || value.get('to') === id)) objects.delete(key)
    })
  }, localOrigin)
}

/** A short description for the object list and screen readers. */
export function describe(object: BoardObject, taskTitle?: string | null): string {
  const kind = objectTypes[object.type]
  if (object.type === 'task') return `${kind}: ${taskTitle ?? 'nicht verfügbar'}`
  if (object.type === 'arrow') return `${kind} von (${Math.round(object.x)}, ${Math.round(object.y)}) nach (${Math.round(object.x2)}, ${Math.round(object.y2)})`
  return object.text.trim() ? `${kind}: ${object.text.trim()}` : `${kind} (${colors[object.color]})`
}

/** Arrows between objects are described by the objects they connect, e.g. "Pfeil: Idee → Umsetzung". */
export function describeArrow(object: BoardObject, objects: BoardObject[], taskTitle: (taskId: string) => string | null | undefined): string {
  if (object.type !== 'arrow' || (!object.from && !object.to)) return describe(object)
  const name = (id: string | null) => {
    const end = id ? objects.find((o) => o.id === id) : undefined
    if (!end) return 'frei'
    const text = end.type === 'task' ? taskTitle(end.taskId ?? '') : end.text.trim()
    return text || `${objectTypes[end.type]} (${colors[end.color]})`
  }
  return `${objectTypes.arrow}: ${name(object.from)} → ${name(object.to)}`
}
