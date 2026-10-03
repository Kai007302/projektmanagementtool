import { describe, expect, it } from 'vitest'
import * as Y from 'yjs'
import { addObject, describe as describeObject, moveBy, objectsOf, readObjects, removeObject, updateObject } from './model'

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
})
