import { forceCenter, forceCollide, forceLink, forceManyBody, forceSimulation, forceX, forceY, type SimulationNodeDatum } from 'd3-force'
import type { Positions } from './graph'

export type LayoutInput = { nodes: { id: string; degree: number; group: string }[]; edges: { source: string; target: string }[] }

type SimNode = SimulationNodeDatum & { id: string; degree: number; group: string }

export const LAYOUT_TICKS = 300

/** Bubble radius in world units: every article is big enough to carry its title, hubs grow. */
export const bubbleRadius = (degree: number) => 34 + Math.sqrt(degree) * 8

/**
 * Force layout of the galaxy. Calls <paramref name="onProgress"/> every few ticks so the view can
 * show the galaxy settling; with reduced motion only the final positions are reported.
 */
export function computeLayout(input: LayoutInput, onProgress: (positions: Positions, done: boolean) => void, progressEvery = 0) {
  const nodes: SimNode[] = input.nodes.map((n, i) => {
    const angle = i * 2.399963 // golden angle: an even spiral as start
    const radius = 40 * Math.sqrt(i)
    return { ...n, x: Math.cos(angle) * radius, y: Math.sin(angle) * radius }
  })
  const ids = new Set(nodes.map((n) => n.id))
  const links = input.edges.filter((e) => ids.has(e.source) && ids.has(e.target)).map((e) => ({ ...e }))

  // Articles of the same type drift towards a shared anchor: the categories form clusters.
  const groups = [...new Set(nodes.map((n) => n.group))]
  const anchor = new Map(groups.map((g, i) => [g, { x: Math.cos((i / groups.length) * Math.PI * 2) * 220, y: Math.sin((i / groups.length) * Math.PI * 2) * 220 }]))

  const simulation = forceSimulation(nodes)
    .force('link', forceLink<SimNode, { source: string; target: string }>(links).id((d) => d.id).distance(130).strength(0.4))
    .force('charge', forceManyBody().strength(-250).distanceMax(700))
    .force('collide', forceCollide<SimNode>((d) => bubbleRadius(d.degree) * (d.degree >= 3 ? 1.6 : 1.15) + 12))
    .force('x', forceX<SimNode>((d) => anchor.get(d.group)!.x).strength(0.1))
    .force('y', forceY<SimNode>((d) => anchor.get(d.group)!.y).strength(0.1))
    .force('center', forceCenter(0, 0))
    .stop()

  const snapshot = () => Object.fromEntries(nodes.map((n) => [n.id, { x: n.x ?? 0, y: n.y ?? 0 }]))
  for (let tick = 1; tick <= LAYOUT_TICKS; tick++) {
    simulation.tick()
    if (progressEvery > 0 && tick % progressEvery === 0 && tick < LAYOUT_TICKS) onProgress(snapshot(), false)
  }
  onProgress(snapshot(), true)
}
