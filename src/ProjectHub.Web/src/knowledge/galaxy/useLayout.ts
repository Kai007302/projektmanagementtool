import { useEffect, useState } from 'react'
import type { KnowledgeGraph, Positions } from './graph'
import { computeLayout, type LayoutInput } from './layout'

/** Lays out the graph in a Web Worker (synchronously where workers are unavailable, e.g. tests). */
export function useLayout(graph: KnowledgeGraph | null, animate: boolean) {
  const [state, setState] = useState<{ graph: KnowledgeGraph | null; positions: Positions; done: boolean }>({ graph: null, positions: {}, done: false })

  useEffect(() => {
    if (!graph) return
    const input: LayoutInput = {
      nodes: graph.nodes.map((n) => ({ id: n.id, degree: n.degree, group: n.articleType })),
      edges: graph.edges.map((e) => ({ source: e.source, target: e.target })),
    }
    const progressEvery = animate ? 10 : 0
    let current = true
    const report = (positions: Positions, done: boolean) => current && setState({ graph, positions, done })

    if (typeof Worker === 'undefined') {
      computeLayout(input, report, 0)
      return () => {
        current = false
      }
    }

    const worker = new Worker(new URL('./layout.worker.ts', import.meta.url), { type: 'module' })
    worker.onmessage = (event: MessageEvent<{ positions: Positions; done: boolean }>) => report(event.data.positions, event.data.done)
    worker.postMessage({ input, progressEvery })
    return () => {
      current = false
      worker.terminate()
    }
  }, [graph, animate])

  return state.graph === graph ? state : { graph, positions: {} as Positions, done: false }
}
