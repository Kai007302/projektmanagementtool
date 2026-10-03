import { describe, expect, it } from 'vitest'
import { computeLayout } from './layout'
import { fitTransform, interpolate, neighborsOf, nodeAt, zoomAt, type GraphNode, type KnowledgeGraph, type Positions } from './graph'

const node = (id: string, degree = 0): GraphNode => ({ id, title: id, articleType: 'article', status: 'published', spaceId: null, summary: null, degree })

describe('galaxy geometry', () => {
  const nodes = [node('a'), node('b')]
  const positions: Positions = { a: { x: 0, y: 0 }, b: { x: 100, y: 0 } }

  it('finds the node under a screen point after pan and zoom', () => {
    const t = { k: 2, x: 50, y: 10 }
    expect(nodeAt(nodes, positions, t, 250, 10)?.id).toBe('b')
    expect(nodeAt(nodes, positions, t, 52, 12)?.id).toBe('a')
    expect(nodeAt(nodes, positions, t, 150, 10)).toBeNull()
  })

  it('zooms around the pointer', () => {
    const t = zoomAt({ k: 1, x: 0, y: 0 }, 2, 100, 0)
    expect(t).toEqual({ k: 2, x: -100, y: 0 })
    expect(zoomAt(t, 100, 0, 0).k).toBe(4)
  })

  it('fits all positions into the viewport', () => {
    const t = fitTransform(Object.values(positions), 300, 200, 50)
    expect(t.x).toBeCloseTo(50)
    expect(100 * t.k + t.x).toBeCloseTo(250)
  })

  it('interpolates transitions from start to end', () => {
    expect(interpolate({ k: 1, x: 0, y: 0 }, { k: 3, x: 10, y: 20 }, 0)).toEqual({ k: 1, x: 0, y: 0 })
    expect(interpolate({ k: 1, x: 0, y: 0 }, { k: 3, x: 10, y: 20 }, 1)).toEqual({ k: 3, x: 10, y: 20 })
  })

  it('lists neighbors in both directions', () => {
    const graph: KnowledgeGraph = {
      nodes: [node('a', 2), node('b', 1), node('c', 1)],
      edges: [
        { id: 'e1', source: 'a', target: 'b', relationType: 'REQUIRES' },
        { id: 'e2', source: 'c', target: 'a', relationType: 'PART_OF' },
      ],
      truncated: false,
    }
    expect(neighborsOf(graph, 'a').map((n) => [n.node.id, n.direction])).toEqual([
      ['b', 'outgoing'],
      ['c', 'incoming'],
    ])
  })
})

describe('galaxy layout', () => {
  it('places hundreds of articles without overlap and keeps related ones close', () => {
    const count = 400
    const input = {
      nodes: Array.from({ length: count }, (_, i) => ({ id: String(i), degree: 1, group: `type-${i % 9}` })),
      edges: Array.from({ length: count - 1 }, (_, i) => ({ source: String(i + 1), target: String(Math.floor(i / 3)) })),
    }
    let result: Positions = {}
    let reports = 0
    const started = performance.now()
    computeLayout(input, (positions, done) => {
      reports++
      if (done) result = positions
    }, 50)
    const elapsed = performance.now() - started

    expect(Object.keys(result)).toHaveLength(count)
    expect(reports).toBeGreaterThan(1)
    expect(elapsed).toBeLessThan(15000)
    const distance = (a: string, b: string) => Math.hypot(result[a].x - result[b].x, result[a].y - result[b].y)
    const linked = input.edges.slice(0, 50).map((e) => distance(e.source, e.target))
    const random = Array.from({ length: 50 }, (_, i) => distance(String(i * 7), String(399 - i * 3)))
    expect(linked.reduce((a, b) => a + b) / 50).toBeLessThan(random.reduce((a, b) => a + b) / 50)
  })
})
