import { describe, expect, it } from 'vitest'
import * as Y from 'yjs'
import { addNextTo, addObject, addObjects, isColor, localOrigin, stickyGrid, describe as describeObject, moveBy, objectsOf, readObjects, removeObject, updateObject } from './model'
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
})

