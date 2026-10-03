import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { articleStatuses, articleTypes, relationTypes, type ArticleType, type Space } from '../api'
import {
  fetchGraph,
  fitTransform,
  focusTransform,
  identity,
  interpolate,
  neighborsOf,
  nodeAt,
  nodeRadius,
  typeColors,
  zoomAt,
  type GraphNode,
  type KnowledgeGraph,
  type Positions,
  type Transform,
} from './graph'
import { useLayout } from './useLayout'

type Props = { spaces: Space[]; onOpenArticle: (id: string) => void }

type View = 'galaxy' | 'list'

const HEIGHT = 560

function usePrefersReducedMotion() {
  const query = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null
  const [reduced, setReduced] = useState(query?.matches ?? false)
  useEffect(() => {
    if (!query) return
    const update = () => setReduced(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [query])
  return reduced
}

/**
 * Knowledge Galaxy: articles as stars, relations as lines, colored by article type.
 * Canvas 2D with a d3-force layout (docs/OPEN_DECISIONS.md, Knowledge Galaxy renderer). The list view
 * shows the same graph for keyboard and screen reader users.
 */
export function KnowledgeGalaxy({ spaces, onOpenArticle }: Props) {
  const [filter, setFilter] = useState<{ spaceId?: string; type?: string }>({})
  const [graph, setGraph] = useState<KnowledgeGraph | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<View>('galaxy')
  const reducedMotion = usePrefersReducedMotion()

  useEffect(() => {
    let current = true
    fetchGraph(filter).then(
      (value) => {
        if (!current) return
        setGraph(value)
        setSelectedId((id) => (id && value.nodes.some((n) => n.id === id) ? id : null))
      },
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [filter])

  const layout = useLayout(graph, !reducedMotion)
  const selected = graph?.nodes.find((n) => n.id === selectedId) ?? null

  return (
    <section className="galaxy" aria-labelledby="galaxy-heading">
      <div className="row">
        <h3 id="galaxy-heading">Wissensgalaxie</h3>
        <nav className="tabs compact" aria-label="Darstellung">
          {(['galaxy', 'list'] as View[]).map((value) => (
            <button
              key={value}
              type="button"
              className={value === view ? 'tab active' : 'tab'}
              aria-current={value === view ? 'page' : undefined}
              onClick={() => setView(value)}
            >
              {value === 'galaxy' ? 'Galaxie' : 'Liste'}
            </button>
          ))}
        </nav>
      </div>
      <div className="inline-form">
        <label>
          Bereich
          <select value={filter.spaceId ?? ''} onChange={(event) => setFilter((f) => ({ ...f, spaceId: event.target.value || undefined }))}>
            <option value="">Alle</option>
            {spaces.map((space) => (
              <option key={space.id} value={space.id}>
                {space.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Art
          <select value={filter.type ?? ''} onChange={(event) => setFilter((f) => ({ ...f, type: event.target.value || undefined }))}>
            <option value="">Alle</option>
            {Object.entries(articleTypes).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        {graph && graph.nodes.length > 0 && (
          <label>
            Artikel fokussieren
            <select value={selectedId ?? ''} onChange={(event) => setSelectedId(event.target.value || null)}>
              <option value="">Keiner</option>
              {[...graph.nodes]
                .sort((a, b) => a.title.localeCompare(b.title, 'de'))
                .map((node) => (
                  <option key={node.id} value={node.id}>
                    {node.title}
                  </option>
                ))}
            </select>
          </label>
        )}
      </div>
      {error && <p role="alert">{error}</p>}
      {graph?.truncated && <p className="muted">Es werden die {graph.nodes.length} zuletzt geänderten Artikel gezeigt. Filter grenzen die Galaxie ein.</p>}
      {!graph ? (
        <p>Galaxie wird geladen …</p>
      ) : graph.nodes.length === 0 ? (
        <p>Keine Artikel für diese Auswahl.</p>
      ) : (
        <div className="galaxy-layout">
          {view === 'galaxy' ? (
            <GalaxyCanvas
              graph={graph}
              positions={layout.positions}
              settled={layout.done}
              selectedId={selectedId}
              reducedMotion={reducedMotion}
              onSelect={setSelectedId}
            />
          ) : (
            <GalaxyList graph={graph} selectedId={selectedId} onSelect={setSelectedId} />
          )}
          <aside className="project-side">
            <GalaxyDetails graph={graph} node={selected} onSelect={setSelectedId} onOpenArticle={onOpenArticle} />
            <Legend graph={graph} />
          </aside>
        </div>
      )}
    </section>
  )
}

type CanvasProps = {
  graph: KnowledgeGraph
  positions: Positions
  settled: boolean
  selectedId: string | null
  reducedMotion: boolean
  onSelect: (id: string | null) => void
}

function GalaxyCanvas({ graph, positions, settled, selectedId, reducedMotion, onSelect }: CanvasProps) {
  const wrapper = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const transform = useRef<Transform>(identity)
  const userMoved = useRef(false)
  const drag = useRef<{ x: number; y: number; start: Transform; moved: boolean } | null>(null)
  const animation = useRef<number | null>(null)
  const frame = useRef<number | null>(null)
  const [width, setWidth] = useState(800)
  const [hoverId, setHoverId] = useState<string | null>(null)

  const neighborIds = useMemo(() => {
    const focus = selectedId ?? hoverId
    return focus ? new Set([focus, ...neighborsOf(graph, focus).map((n) => n.node.id)]) : null
  }, [graph, selectedId, hoverId])

  const draw = useCallback(() => {
    frame.current = null
    const element = canvas.current
    const ctx = element?.getContext('2d')
    if (!element || !ctx) return
    const dpr = window.devicePixelRatio || 1
    const t = transform.current
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, HEIGHT)
    ctx.setTransform(dpr * t.k, 0, 0, dpr * t.k, dpr * t.x, dpr * t.y)

    const incident = (source: string, target: string) => neighborIds !== null && (source === (selectedId ?? hoverId) || target === (selectedId ?? hoverId))
    ctx.lineWidth = 1 / t.k
    ctx.strokeStyle = neighborIds ? 'rgba(90, 101, 115, 0.12)' : 'rgba(90, 101, 115, 0.35)'
    ctx.beginPath()
    for (const edge of graph.edges) {
      const a = positions[edge.source]
      const b = positions[edge.target]
      if (!a || !b || incident(edge.source, edge.target)) continue
      ctx.moveTo(a.x, a.y)
      ctx.lineTo(b.x, b.y)
    }
    ctx.stroke()
    if (neighborIds) {
      ctx.lineWidth = 2 / t.k
      ctx.strokeStyle = 'rgba(42, 91, 215, 0.8)'
      ctx.beginPath()
      for (const edge of graph.edges) {
        const a = positions[edge.source]
        const b = positions[edge.target]
        if (!a || !b || !incident(edge.source, edge.target)) continue
        ctx.moveTo(a.x, a.y)
        ctx.lineTo(b.x, b.y)
      }
      ctx.stroke()
    }

    // One path per type and emphasis keeps hundreds of nodes cheap to draw.
    for (const dimmed of [true, false]) {
      ctx.globalAlpha = dimmed ? 0.2 : 1
      for (const type of Object.keys(typeColors) as ArticleType[]) {
        ctx.fillStyle = typeColors[type]
        ctx.beginPath()
        for (const node of graph.nodes) {
          const p = positions[node.id]
          if (!p || node.articleType !== type || (neighborIds !== null && !neighborIds.has(node.id)) !== dimmed) continue
          const r = nodeRadius(node)
          ctx.moveTo(p.x + r, p.y)
          ctx.arc(p.x, p.y, r, 0, Math.PI * 2)
        }
        ctx.fill()
      }
    }
    ctx.globalAlpha = 1

    for (const [id, color, width] of [
      [hoverId, '#1c2430', 1.5],
      [selectedId, '#1c2430', 3],
    ] as const) {
      const node = id ? graph.nodes.find((n) => n.id === id) : null
      const p = node ? positions[node.id] : null
      if (!node || !p) continue
      ctx.lineWidth = width / t.k
      ctx.strokeStyle = color
      ctx.beginPath()
      ctx.arc(p.x, p.y, nodeRadius(node) + 3 / t.k, 0, Math.PI * 2)
      ctx.stroke()
    }

    // Labels: all when zoomed in or few articles, otherwise only around the focus.
    const showAll = graph.nodes.length <= 40 || t.k >= 1.6
    ctx.font = `${12 / t.k}px system-ui, sans-serif`
    ctx.fillStyle = '#1c2430'
    for (const node of graph.nodes) {
      const p = positions[node.id]
      if (!p || !(showAll || neighborIds?.has(node.id))) continue
      if (neighborIds && !neighborIds.has(node.id)) continue
      const sx = p.x * t.k + t.x
      const sy = p.y * t.k + t.y
      if (sx < -200 || sx > width + 20 || sy < -20 || sy > HEIGHT + 20) continue
      ctx.fillText(node.title, p.x + nodeRadius(node) + 4 / t.k, p.y + 4 / t.k)
    }
  }, [graph, positions, width, neighborIds, selectedId, hoverId])

  const requestDraw = useCallback(() => {
    if (frame.current === null) frame.current = requestAnimationFrame(draw)
  }, [draw])

  useEffect(requestDraw, [requestDraw])

  // Reset the refs as well: StrictMode unmounts and mounts again, and a stale id would block drawing.
  useEffect(
    () => () => {
      if (frame.current !== null) cancelAnimationFrame(frame.current)
      if (animation.current !== null) cancelAnimationFrame(animation.current)
      frame.current = null
      animation.current = null
    },
    [],
  )

  const setTransform = useCallback(
    (next: Transform) => {
      transform.current = next
      requestDraw()
    },
    [requestDraw],
  )

  /** Moves the view, animated unless the user prefers reduced motion. */
  const animateTo = useCallback(
    (target: Transform) => {
      if (animation.current !== null) cancelAnimationFrame(animation.current)
      // data-view tells tests (and anyone curious) when the view has arrived.
      const element = canvas.current
      if (reducedMotion) {
        setTransform(target)
        if (element) element.dataset.view = 'idle'
        return
      }
      if (element) element.dataset.view = 'moving'
      const from = transform.current
      const start = performance.now()
      const step = (now: number) => {
        const progress = Math.min(1, (now - start) / 450)
        setTransform(interpolate(from, target, progress))
        animation.current = progress < 1 ? requestAnimationFrame(step) : null
        if (progress === 1 && element) element.dataset.view = 'idle'
      }
      animation.current = requestAnimationFrame(step)
    },
    [reducedMotion, setTransform],
  )

  const fit = useCallback(() => setTransform(fitTransform(Object.values(positions), width, HEIGHT)), [positions, width, setTransform])

  // Follow the settling layout until the person moves the view themselves.
  useEffect(() => {
    if (!userMoved.current && !selectedId) fit()
  }, [fit, selectedId])

  // Focus the selected article.
  useEffect(() => {
    const p = selectedId ? positions[selectedId] : null
    if (p && settled) animateTo(focusTransform(p, width, HEIGHT, Math.max(transform.current.k, 1.8)))
  }, [selectedId, settled, positions, width, animateTo])

  useEffect(() => {
    const element = wrapper.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(320, Math.floor(entry.contentRect.width))))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const element = canvas.current
    if (!element) return
    const wheel = (event: WheelEvent) => {
      event.preventDefault()
      userMoved.current = true
      const rect = element.getBoundingClientRect()
      setTransform(zoomAt(transform.current, Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top))
    }
    element.addEventListener('wheel', wheel, { passive: false })
    return () => element.removeEventListener('wheel', wheel)
  }, [setTransform])

  function point(event: PointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  function pointerDown(event: PointerEvent<HTMLCanvasElement>) {
    event.currentTarget.setPointerCapture?.(event.pointerId)
    const p = point(event)
    drag.current = { ...p, start: transform.current, moved: false }
  }

  function pointerMove(event: PointerEvent<HTMLCanvasElement>) {
    const p = point(event)
    const current = drag.current
    if (current) {
      const dx = p.x - current.x
      const dy = p.y - current.y
      if (current.moved || Math.hypot(dx, dy) > 4) {
        current.moved = true
        userMoved.current = true
        setTransform({ ...current.start, x: current.start.x + dx, y: current.start.y + dy })
      }
      return
    }
    const node = nodeAt(graph.nodes, positions, transform.current, p.x, p.y)
    setHoverId(node?.id ?? null)
  }

  function pointerUp(event: PointerEvent<HTMLCanvasElement>) {
    const current = drag.current
    drag.current = null
    if (current && !current.moved) {
      const p = point(event)
      onSelect(nodeAt(graph.nodes, positions, transform.current, p.x, p.y)?.id ?? null)
    }
  }

  function keyDown(event: KeyboardEvent<HTMLCanvasElement>) {
    const t = transform.current
    const step = 60
    const moves: Record<string, () => Transform> = {
      ArrowLeft: () => ({ ...t, x: t.x + step }),
      ArrowRight: () => ({ ...t, x: t.x - step }),
      ArrowUp: () => ({ ...t, y: t.y + step }),
      ArrowDown: () => ({ ...t, y: t.y - step }),
      '+': () => zoomAt(t, 1.25, width / 2, HEIGHT / 2),
      '-': () => zoomAt(t, 0.8, width / 2, HEIGHT / 2),
    }
    if (event.key in moves) {
      event.preventDefault()
      userMoved.current = true
      setTransform(moves[event.key]())
    } else if (event.key === '0') {
      event.preventDefault()
      fit()
    } else if (event.key === 'Escape') {
      onSelect(null)
    }
  }

  const hovered = hoverId ? graph.nodes.find((n) => n.id === hoverId) : null
  const dpr = typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1

  return (
    <div className="galaxy-canvas" ref={wrapper}>
      <canvas
        ref={canvas}
        width={width * dpr}
        height={HEIGHT * dpr}
        style={{ width, height: HEIGHT }}
        tabIndex={0}
        role="img"
        aria-label={`Wissensgalaxie mit ${graph.nodes.length} ${graph.nodes.length === 1 ? 'Artikel' : 'Artikeln'} und ${graph.edges.length} ${graph.edges.length === 1 ? 'Beziehung' : 'Beziehungen'}`}
        aria-describedby="galaxy-help"
        className={hovered ? 'pointer' : undefined}
        data-layout={settled ? 'done' : 'running'}
        onPointerDown={pointerDown}
        onPointerMove={pointerMove}
        onPointerUp={pointerUp}
        onPointerLeave={() => setHoverId(null)}
        onKeyDown={keyDown}
      />
      {hovered && (
        <div className="galaxy-tooltip" aria-hidden="true">
          <strong>{hovered.title}</strong> · {articleTypes[hovered.articleType]}
        </div>
      )}
      <div className="galaxy-controls">
        <button type="button" aria-label="Vergrößern" onClick={() => animateTo(zoomAt(transform.current, 1.4, width / 2, HEIGHT / 2))}>
          +
        </button>
        <button type="button" aria-label="Verkleinern" onClick={() => animateTo(zoomAt(transform.current, 1 / 1.4, width / 2, HEIGHT / 2))}>
          −
        </button>
        <button
          type="button"
          onClick={() => {
            userMoved.current = false
            animateTo(fitTransform(Object.values(positions), width, HEIGHT))
          }}
        >
          Alles zeigen
        </button>
      </div>
      <p id="galaxy-help" className="muted">
        Ziehen verschiebt, Mausrad zoomt, Klick wählt einen Artikel. Tastatur: Pfeiltasten, + und −, 0 zeigt alles, Esc hebt die Auswahl auf. Die Darstellung „Liste“ zeigt
        dieselben Inhalte.
      </p>
    </div>
  )
}

function GalaxyList({ graph, selectedId, onSelect }: { graph: KnowledgeGraph; selectedId: string | null; onSelect: (id: string) => void }) {
  const groups = (Object.keys(articleTypes) as ArticleType[])
    .map((type) => ({ type, nodes: graph.nodes.filter((n) => n.articleType === type).sort((a, b) => a.title.localeCompare(b.title, 'de')) }))
    .filter((group) => group.nodes.length > 0)

  return (
    <div className="galaxy-list">
      {groups.map((group) => (
        <section key={group.type} aria-labelledby={`galaxy-group-${group.type}`}>
          <h4 id={`galaxy-group-${group.type}`}>
            <span className="legend-dot" style={{ background: typeColors[group.type] }} aria-hidden="true" /> {articleTypes[group.type]} ({group.nodes.length})
          </h4>
          <ul className="plain-list">
            {group.nodes.map((node) => (
              <li key={node.id}>
                <button type="button" className="link-button" aria-pressed={node.id === selectedId} onClick={() => onSelect(node.id)}>
                  {node.title}
                </button>
                <small className="muted"> · {node.degree === 1 ? '1 Beziehung' : `${node.degree} Beziehungen`}</small>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}

type DetailsProps = { graph: KnowledgeGraph; node: GraphNode | null; onSelect: (id: string) => void; onOpenArticle: (id: string) => void }

function GalaxyDetails({ graph, node, onSelect, onOpenArticle }: DetailsProps) {
  if (!node) {
    return (
      <section className="panel" aria-labelledby="galaxy-details-heading">
        <h3 id="galaxy-details-heading">Auswahl</h3>
        <p className="muted">
          {graph.nodes.length} Artikel, {graph.edges.length} Beziehungen. Wähle einen Artikel, um seine Beziehungen zu sehen.
        </p>
      </section>
    )
  }

  const neighbors = neighborsOf(graph, node.id)
  return (
    <section className="panel" aria-labelledby="galaxy-details-heading">
      <h3 id="galaxy-details-heading" aria-live="polite">
        {node.title}
      </h3>
      <p className="muted">
        <span className="legend-dot" style={{ background: typeColors[node.articleType] }} aria-hidden="true" /> {articleTypes[node.articleType]} ·{' '}
        {articleStatuses[node.status]}
      </p>
      {node.summary && <p>{node.summary}</p>}
      <button type="button" onClick={() => onOpenArticle(node.id)}>
        Artikel öffnen
      </button>
      <h4>Beziehungen</h4>
      {neighbors.length === 0 ? (
        <p className="muted">Keine sichtbaren Beziehungen.</p>
      ) : (
        <ul className="plain-list">
          {neighbors.map(({ edge, node: other, direction }) => (
            <li key={edge.id}>
              <small className="muted">{relationTypes[edge.relationType][direction]}</small>{' '}
              <button type="button" className="link-button" onClick={() => onSelect(other.id)}>
                {other.title}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function Legend({ graph }: { graph: KnowledgeGraph }) {
  const present = new Set(graph.nodes.map((n) => n.articleType))
  return (
    <section className="panel" aria-labelledby="galaxy-legend-heading">
      <h3 id="galaxy-legend-heading">Legende</h3>
      <ul className="plain-list legend">
        {(Object.keys(articleTypes) as ArticleType[])
          .filter((type) => present.has(type))
          .map((type) => (
            <li key={type}>
              <span className="legend-dot" style={{ background: typeColors[type] }} aria-hidden="true" /> {articleTypes[type]}
            </li>
          ))}
      </ul>
      <p className="muted">Größere Sterne haben mehr Beziehungen.</p>
    </section>
  )
}
