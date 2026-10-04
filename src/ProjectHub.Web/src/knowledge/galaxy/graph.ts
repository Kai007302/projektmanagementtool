import { apiFetch } from '../../api/client'
import type { ArticleStatus, ArticleType, RelationType } from '../api'
import { bubbleRadius } from './layout'

export type GraphNode = {
  id: string
  title: string
  articleType: ArticleType
  status: ArticleStatus
  spaceId: string | null
  summary: string | null
  degree: number
}

export type GraphEdge = { id: string; source: string; target: string; relationType: RelationType }

export type KnowledgeGraph = { nodes: GraphNode[]; edges: GraphEdge[]; truncated: boolean }

export function fetchGraph(filter: { spaceId?: string; type?: string }) {
  const query = new URLSearchParams()
  if (filter.spaceId) query.set('spaceId', filter.spaceId)
  if (filter.type) query.set('type', filter.type)
  const suffix = query.size > 0 ? `?${query}` : ''
  return apiFetch<KnowledgeGraph>(`/api/v1/knowledge/graph${suffix}`)
}

/** Colors per article type: the categories of the galaxy (legend in the view). Vivid, to glow on the dark space. */
export const typeColors: Record<ArticleType, string> = {
  article: '#6366f1',
  how_to: '#06b6d4',
  best_practice: '#10b981',
  process: '#a855f7',
  policy: '#f43f5e',
  faq: '#f59e0b',
  template: '#f97316',
  checklist: '#ec4899',
  glossary: '#94a3b8',
}

export type Point = { x: number; y: number }

export type Positions = Record<string, Point>

/** Screen = world * k + (x, y). */
export type Transform = { k: number; x: number; y: number }

export const identity: Transform = { k: 1, x: 0, y: 0 }

export const nodeRadius = (node: Pick<GraphNode, 'degree'>) => bubbleRadius(node.degree)

export const toWorld = (t: Transform, sx: number, sy: number): Point => ({ x: (sx - t.x) / t.k, y: (sy - t.y) / t.k })

/** The node under a screen point, if any (with a few pixels of tolerance). */
export function nodeAt(nodes: GraphNode[], positions: Positions, t: Transform, sx: number, sy: number): GraphNode | null {
  const p = toWorld(t, sx, sy)
  let best: GraphNode | null = null
  let bestDistance = Infinity
  for (const node of nodes) {
    const q = positions[node.id]
    if (!q) continue
    const distance = Math.hypot(q.x - p.x, q.y - p.y)
    if (distance <= nodeRadius(node) + 4 / t.k && distance < bestDistance) {
      best = node
      bestDistance = distance
    }
  }
  return best
}

/** Zooms by <paramref name="factor"/> keeping the screen point (sx, sy) fixed. */
export function zoomAt(t: Transform, factor: number, sx: number, sy: number, min = 0.1, max = 4): Transform {
  const k = Math.min(max, Math.max(min, t.k * factor))
  const p = toWorld(t, sx, sy)
  return { k, x: sx - p.x * k, y: sy - p.y * k }
}

/** Shows all positions inside a width × height viewport. */
export function fitTransform(positions: Point[], width: number, height: number, padding = 40): Transform {
  if (positions.length === 0) return identity
  const xs = positions.map((p) => p.x)
  const ys = positions.map((p) => p.y)
  const minX = Math.min(...xs)
  const maxX = Math.max(...xs)
  const minY = Math.min(...ys)
  const maxY = Math.max(...ys)
  const k = Math.min(2, (width - padding * 2) / Math.max(1, maxX - minX), (height - padding * 2) / Math.max(1, maxY - minY))
  return { k, x: width / 2 - ((minX + maxX) / 2) * k, y: height / 2 - ((minY + maxY) / 2) * k }
}

/** Centers a point at zoom k. */
export const focusTransform = (p: Point, width: number, height: number, k: number): Transform => ({ k, x: width / 2 - p.x * k, y: height / 2 - p.y * k })

export function interpolate(a: Transform, b: Transform, t: number): Transform {
  const e = t < 0.5 ? 2 * t * t : 1 - (-2 * t + 2) ** 2 / 2
  return { k: a.k + (b.k - a.k) * e, x: a.x + (b.x - a.x) * e, y: a.y + (b.y - a.y) * e }
}

export type Neighbor = { edge: GraphEdge; node: GraphNode; direction: 'outgoing' | 'incoming' }

export function neighborsOf(graph: KnowledgeGraph, id: string): Neighbor[] {
  const byId = new Map(graph.nodes.map((n) => [n.id, n]))
  return graph.edges.flatMap((edge): Neighbor[] => {
    if (edge.source === id && byId.has(edge.target)) return [{ edge, node: byId.get(edge.target)!, direction: 'outgoing' }]
    if (edge.target === id && byId.has(edge.source)) return [{ edge, node: byId.get(edge.source)!, direction: 'incoming' }]
    return []
  })
}
