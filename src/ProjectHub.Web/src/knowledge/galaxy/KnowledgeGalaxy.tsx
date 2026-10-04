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
import { createSpace, drawSpace, float, wrapTitle } from './space'
import { useLayout } from './useLayout'

type Props = { spaces: Space[]; onOpenArticle: (id: string) => void }

type View = 'galaxy' | 'list'

const HEIGHT = 600
/** Room around the outermost bubbles when the whole galaxy is shown. */
const FIT_PADDING = 90

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
 * Knowledge Galaxy: articles as glowing bubbles with their title, relations as threads of light,
 * colored by article type, floating in a violet space.
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

  const space = useMemo(() => createSpace(), [])
  const titleCache = useRef(new Map<string, string[]>())

  /** Paints one frame; <paramref name="time"/> drives the floating and twinkling (0 = still). */
  const draw = useCallback(
    (time: number) => {
      frame.current = null
      const element = canvas.current
      const ctx = element?.getContext('2d')
      if (!element || !ctx) return
      const dpr = window.devicePixelRatio || 1
      const t = transform.current
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      drawSpace(ctx, space, width, HEIGHT, time, t)
      ctx.setTransform(dpr * t.k, 0, 0, dpr * t.k, dpr * t.x, dpr * t.y)

      const focus = selectedId ?? hoverId
      const at = (id: string) => {
        const p = positions[id]
        if (!p) return null
        const offset = float(id, time)
        return { x: p.x + offset.x, y: p.y + offset.y }
      }

      // Relations: softly curved threads of light; the ones of the focused article shine brighter.
      const glow = graph.nodes.length <= 150
      for (const highlighted of [false, true]) {
        if (highlighted && !focus) continue
        ctx.lineWidth = (highlighted ? 2.5 : 1.4) / t.k
        ctx.strokeStyle = highlighted ? 'rgba(251, 207, 232, 0.95)' : focus ? 'rgba(244, 170, 255, 0.12)' : 'rgba(244, 170, 255, 0.4)'
        ctx.shadowColor = 'rgba(236, 72, 153, 0.9)'
        ctx.shadowBlur = highlighted && glow ? 12 : 0
        ctx.beginPath()
        for (const edge of graph.edges) {
          const a = at(edge.source)
          const b = at(edge.target)
          if (!a || !b || (edge.source === focus || edge.target === focus) !== highlighted) continue
          const bend = 0.12
          ctx.moveTo(a.x, a.y)
          ctx.quadraticCurveTo((a.x + b.x) / 2 - (b.y - a.y) * bend, (a.y + b.y) / 2 + (b.x - a.x) * bend, b.x, b.y)
        }
        ctx.stroke()
      }
      ctx.shadowBlur = 0

      // Bubbles: glowing spheres in the color of their type, the title inside.
      const showAllLabels = graph.nodes.length <= 40 || t.k >= 1.6
      for (const node of graph.nodes) {
        const p = at(node.id)
        if (!p) continue
        const r = nodeRadius(node)
        const sx = p.x * t.k + t.x
        const sy = p.y * t.k + t.y
        const sr = r * t.k
        if (sx < -sr - 200 || sx > width + sr || sy < -sr || sy > HEIGHT + sr) continue
        const color = typeColors[node.articleType]
        const dimmed = neighborIds !== null && !neighborIds.has(node.id)
        ctx.globalAlpha = dimmed ? 0.25 : 1

        ctx.shadowColor = color
        ctx.shadowBlur = glow && !dimmed ? Math.min(40, 18 + sr * 0.3) : 0
        ctx.fillStyle = color
        ctx.beginPath()
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2)
        ctx.fill()
        ctx.shadowBlur = 0

        const shine = ctx.createRadialGradient(p.x - r * 0.35, p.y - r * 0.4, r * 0.1, p.x, p.y, r)
        shine.addColorStop(0, 'rgba(255, 255, 255, 0.5)')
        shine.addColorStop(0.6, 'rgba(255, 255, 255, 0.06)')
        shine.addColorStop(1, 'rgba(18, 5, 42, 0.25)')
        ctx.fillStyle = shine
        ctx.fill()
        ctx.lineWidth = 1.2 / t.k
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.45)'
        ctx.stroke()

        if (node.id === hoverId || node.id === selectedId) {
          ctx.lineWidth = (node.id === selectedId ? 3 : 1.5) / t.k
          ctx.strokeStyle = '#ffffff'
          ctx.beginPath()
          ctx.arc(p.x, p.y, r + 5 / t.k, 0, Math.PI * 2)
          ctx.stroke()
        }

        if (dimmed) continue
        if (sr >= 22) {
          const px = Math.min(15, Math.max(9, sr * 0.24))
          ctx.font = `600 ${px / t.k}px system-ui, sans-serif`
          const key = `${node.id}:${px.toFixed(1)}:${t.k.toFixed(3)}:${node.title}`
          let lines = titleCache.current.get(key)
          if (!lines) {
            if (titleCache.current.size > 2000) titleCache.current.clear()
            lines = wrapTitle(ctx, node.title, r * 1.7, sr >= 30 ? 3 : 2)
            titleCache.current.set(key, lines)
          }
          const lineHeight = (px * 1.2) / t.k
          ctx.fillStyle = '#ffffff'
          ctx.textAlign = 'center'
          ctx.textBaseline = 'middle'
          ctx.shadowColor = 'rgba(18, 5, 42, 0.8)'
          ctx.shadowBlur = 4
          lines.forEach((line, i) => ctx.fillText(line, p.x, p.y + (i - (lines.length - 1) / 2) * lineHeight))
          ctx.shadowBlur = 0
          ctx.textAlign = 'start'
          ctx.textBaseline = 'alphabetic'
        } else if (showAllLabels || neighborIds?.has(node.id)) {
          ctx.font = `${12 / t.k}px system-ui, sans-serif`
          ctx.fillStyle = 'rgba(255, 255, 255, 0.9)'
          ctx.fillText(node.title, p.x + r + 4 / t.k, p.y + 4 / t.k)
        }
      }
      ctx.globalAlpha = 1
    },
    [graph, positions, width, neighborIds, selectedId, hoverId, space],
  )

  // Without reduced motion a frame loop keeps the bubbles floating; otherwise draw on change only.
  const drawLatest = useRef(draw)
  useEffect(() => {
    drawLatest.current = draw
  }, [draw])

  useEffect(() => {
    if (reducedMotion) return
    let id = requestAnimationFrame(function loop(now) {
      drawLatest.current(now)
      id = requestAnimationFrame(loop)
    })
    return () => cancelAnimationFrame(id)
  }, [reducedMotion])

  const requestDraw = useCallback(() => {
    if (reducedMotion && frame.current === null) frame.current = requestAnimationFrame(() => drawLatest.current(0))
  }, [reducedMotion])

  useEffect(requestDraw, [requestDraw, draw])


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

  const fit = useCallback(() => setTransform(fitTransform(Object.values(positions), width, HEIGHT, FIT_PADDING)), [positions, width, setTransform])

  // Follow the settling layout until the person moves the view themselves.
  useEffect(() => {
    if (!userMoved.current && !selectedId) fit()
  }, [fit, selectedId])

  // Focus the selected article.
  useEffect(() => {
    const p = selectedId ? positions[selectedId] : null
    if (p && settled) animateTo(focusTransform(p, width, HEIGHT, Math.max(transform.current.k, 1.2)))
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
            animateTo(fitTransform(Object.values(positions), width, HEIGHT, FIT_PADDING))
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
      <p className="muted">Größere Kugeln haben mehr Beziehungen.</p>
    </section>
  )
}
