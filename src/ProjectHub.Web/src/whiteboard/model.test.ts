import { describe, expect, it } from 'vitest'
import * as Y from 'yjs'
import {
  addConnector,
  addNextTo,
  addObject,
  addObjects,
  isColor,
  localOrigin,
  gridBackground,
  stickyGrid,
  describe as describeObject,
  describeArrow,
  moveBy,
  objectsOf,
  readObjects,
  removeObject,
  updateObject,
} from './model'
import { templates } from './templates'

describe('whiteboard model', () => {
  it('adds objects centered on a point and changes single fields', () => {
    const doc = new Y.Doc()
    const id = addObject(doc, 'sticky', { x: 100, y: 100 })
    updateObject(doc, id, { text: 'Hallo', w: 5 })

    const [sticky] = readObjects(doc)
    expect(sticky).toMatchObject({ id, type: 'sticky', x: 10, y: 40, w: 20, h: 120, color: 'yellow', text: 'Hallo' })
    expect(describeObject(sticky)).toBe('Notiz: Hallo')
  })

  it('moves arrows at both ends and removes objects', () => {
    const doc = new Y.Doc()
    const id = addObject(doc, 'arrow', { x: 0, y: 0 })
    const [arrow] = readObjects(doc)
    updateObject(doc, id, moveBy(arrow, 10, 5))

    expect(readObjects(doc)[0]).toMatchObject({ x: -50, y: 5, x2: 70, y2: 5 })
    removeObject(doc, id)
    expect(readObjects(doc)).toEqual([])
  })

  it('keeps concurrent changes to different fields of the same object', () => {
    const a = new Y.Doc()
    const b = new Y.Doc()
    const id = addObject(a, 'rect', { x: 0, y: 0 })
    Y.applyUpdate(b, Y.encodeStateAsUpdate(a))

    updateObject(a, id, { text: 'von A' })
    updateObject(b, id, { x: 300 })
    Y.applyUpdate(a, Y.encodeStateAsUpdate(b))
    Y.applyUpdate(b, Y.encodeStateAsUpdate(a))

    expect(readObjects(a)).toEqual(readObjects(b))
    expect(readObjects(a)[0]).toMatchObject({ text: 'von A', x: 300 })
  })

  it('ignores unknown types and replaces unusable values from other clients', () => {
    const doc = new Y.Doc()
    const weird = new Y.Map<unknown>()
    weird.set('type', 'sticky')
    weird.set('x', 'links')
    weird.set('color', 'neon')
    objectsOf(doc).set('w1', weird)
    const unknown = new Y.Map<unknown>()
    unknown.set('type', 'video')
    objectsOf(doc).set('w2', unknown)
    objectsOf(doc).set('w3', 'kein Objekt' as unknown as Y.Map<unknown>)

    expect(readObjects(doc)).toEqual([expect.objectContaining({ id: 'w1', x: 0, color: 'yellow', w: 180 })])
  })

  it('describes task cards by their live title', () => {
    const doc = new Y.Doc()
    addObject(doc, 'task', { x: 0, y: 0 }, 't-1')
    const [card] = readObjects(doc)

    expect(card.taskId).toBe('t-1')
    expect(describeObject(card, 'Startseite')).toBe('Aufgabe: Startseite')
    expect(describeObject(card, null)).toBe('Aufgabe: nicht verfügbar')
  })

  it('adds several objects centered on a point in one change', () => {
    const doc = new Y.Doc()
    const undo = new Y.UndoManager(objectsOf(doc), { trackedOrigins: new Set([localOrigin]) })
    const { ids, bounds } = addObjects(
      doc,
      [
        { type: 'rect', x: 0, y: 0, w: 200, h: 100, text: 'Rahmen' },
        { type: 'arrow', x: 0, y: 150, x2: 400, y2: 150 },
        { type: 'diamond', x: 300, y: 0 },
      ],
      { x: 1000, y: 1000 },
    )

    expect(ids).toHaveLength(3)
    expect(bounds).toEqual({ x: 770, y: 925, w: 460, h: 150 })
    expect(readObjects(doc)).toEqual([
      expect.objectContaining({ type: 'rect', x: 770, y: 925, w: 200, h: 100, color: 'blue', text: 'Rahmen' }),
      expect.objectContaining({ type: 'diamond', x: 1070, y: 925, w: 160, h: 120, color: 'yellow' }),
      expect.objectContaining({ type: 'arrow', x: 770, y: 1075, x2: 1170, y2: 1075 }),
    ])
    undo.undo()
    expect(readObjects(doc)).toEqual([])
  })
})

describe('sticky notes', () => {
  it('adds an empty note of the same size and color next to another one', () => {
    const doc = new Y.Doc()
    addObjects(doc, [{ type: 'sticky', x: 0, y: 0, w: 100, h: 80, color: 'navy', text: 'A' }], { x: 50, y: 40 })
    const [first] = readObjects(doc)
    addNextTo(doc, first, 'right')
    addNextTo(doc, first, 'below')

    expect(readObjects(doc)).toEqual([
      expect.objectContaining({ x: 0, y: 0, text: 'A' }),
      expect.objectContaining({ x: 120, y: 0, w: 100, h: 80, color: 'navy', text: '' }),
      expect.objectContaining({ x: 0, y: 100, w: 100, h: 80, color: 'navy', text: '' }),
    ])
  })

  it('lays out one note per non-empty line in a grid', () => {
    const items = stickyGrid(['Eins', ' ', 'Zwei', 'Drei', 'Vier', 'Fünf'], 'teal')
    expect(items.map((i) => i.text)).toEqual(['Eins', 'Zwei', 'Drei', 'Vier', 'Fünf'])
    expect(items.map((i) => [i.x, i.y])).toEqual([[0, 0], [200, 0], [400, 0], [0, 140], [200, 140]])
    expect(items.every((i) => i.color === 'teal' && i.type === 'sticky')).toBe(true)
    expect(stickyGrid(Array.from({ length: 150 }, (_, i) => `${i}`), 'yellow')).toHaveLength(100)
  })

  it('accepts only known colors', () => {
    expect(isColor('purple')).toBe(true)
    expect(isColor('toString')).toBe(false)
    expect(isColor(3)).toBe(false)
  })
})

describe('whiteboard connectors and locks', () => {
  function two() {
    const doc = new Y.Doc()
    addObject(doc, 'sticky', { x: 100, y: 100 })
    addObject(doc, 'rect', { x: 600, y: 100 })
    const [a, b] = readObjects(doc)
    return { doc, a, b }
  }

  it('hangs an arrow on two objects and keeps its ends on their outlines', () => {
    const { doc, a, b } = two()
    addConnector(doc, a, b)

    const arrow = readObjects(doc).find((o) => o.type === 'arrow')!
    expect(arrow).toMatchObject({ from: a.id, to: b.id })
    expect(arrow.x).toBeGreaterThan(a.x + a.w - 1)
    expect(arrow.x2).toBeLessThan(b.x + 1)

    // The note moves, the arrow follows without anyone changing the arrow.
    updateObject(doc, a.id, { x: a.x, y: a.y + 300 })
    const moved = readObjects(doc).find((o) => o.type === 'arrow')!
    expect(moved.y).toBeGreaterThan(arrow.y + 100)
    expect(describeArrow(moved, readObjects(doc), () => null)).toBe('Pfeil: Neue Notiz → Rechteck (Blau)')
  })

  it('draws an arrow to a free point and moves it with the object it hangs on', () => {
    const { doc, a } = two()
    const id = addConnector(doc, a, { x: 400, y: 400 })

    const arrow = readObjects(doc).find((o) => o.id === id)!
    expect(arrow).toMatchObject({ from: a.id, to: null, x2: 400, y2: 400 })
    // Dragging the arrow itself moves only the loose end; the other one stays on its object.
    updateObject(doc, id, moveBy(arrow, 50, 50))
    expect(readObjects(doc).find((o) => o.id === id)).toMatchObject({ x2: 450, y2: 450, x: arrow.x, y: arrow.y })
  })

  it('removes the arrows of a removed object and forgets an end that is gone', () => {
    const { doc, a, b } = two()
    addConnector(doc, a, b)
    removeObject(doc, b.id)

    expect(readObjects(doc).map((o) => o.type)).toEqual(['sticky'])
  })

  it('stores the lock in the document and drops the field when it is unlocked', () => {
    const { doc, a } = two()
    updateObject(doc, a.id, { locked: true })
    expect(readObjects(doc).find((o) => o.id === a.id)?.locked).toBe(true)

    updateObject(doc, a.id, { locked: false })
    expect(readObjects(doc).find((o) => o.id === a.id)?.locked).toBe(false)
    expect(objectsOf(doc).get(a.id)?.has('locked')).toBe(false)
  })
})

describe('whiteboard templates', () => {
  it('offers ten templates with distinct ids and names', () => {
    expect(templates).toHaveLength(10)
    expect(new Set(templates.map((t) => t.id)).size).toBe(10)
    expect(new Set(templates.map((t) => t.name)).size).toBe(10)
  })

  it.each(templates.map((t) => [t.name, t] as const))('%s draws frames before what lies on them', (_name, template) => {
    const doc = new Y.Doc()
    addObjects(doc, template.items, { x: 0, y: 0 })
    const objects = readObjects(doc)
    expect(objects).toHaveLength(template.items.length)

    // Rendering follows readObjects order, so anything inside a rectangle must come after it.
    const inside = (o: (typeof objects)[number], f: (typeof objects)[number]) =>
      o !== f && o.type !== 'arrow' && o.x >= f.x && o.y >= f.y && o.x + o.w <= f.x + f.w && o.y + o.h <= f.y + f.h
    objects.forEach((frame, index) => {
      if (frame.type !== 'rect') return
      objects.slice(0, index).forEach((other) => expect(inside(other, frame), `${describeObject(other)} in ${describeObject(frame)}`).toBe(false))
    })
  })

  it('moves and scales the background dots with the view', () => {
    expect(gridBackground({ x: 0, y: 0, zoom: 1 })).toEqual({ backgroundSize: '24px 24px', backgroundPosition: '-12px -12px' })
    expect(gridBackground({ x: 100, y: -40, zoom: 2 })).toEqual({ backgroundSize: '48px 48px', backgroundPosition: '76px -64px' })
    expect(gridBackground({ x: 0, y: 0, zoom: 0.25 }).backgroundSize).toBe('12px 12px')
    expect(gridBackground({ x: 0, y: 0, zoom: 0.2 }).backgroundSize).toBe('19.2px 19.2px')
  })
})
