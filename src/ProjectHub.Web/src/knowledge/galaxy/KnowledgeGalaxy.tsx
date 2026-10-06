import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
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
import { GalaxyReader } from './GalaxyReader'
import { drawPlanet } from './planets'
import { createSpace, drawSpace, float, wrapTitle } from './space'
import { useLayout } from './useLayout'

type Props = { spaces: Space[]; onOpenArticle: (id: string) => void }

type View = 'galaxy' | 'list'

const HEIGHT = 600
/** Room around the outermost bubbles when the whole galaxy is shown. */
const FIT_PADDING = 90
/** A planet this big on screen (radius in px) counts as zoomed into: its article opens beside the galaxy. */
const OPEN_RADIUS = 110
/** Two clicks or taps on the same planet within this time (ms) open its article. */
const DOUBLE_CLICK_MS = 450

function useMediaQuery(media: string) {
  const query = useMemo(() => (typeof window !== 'undefined' && window.matchMedia ? window.matchMedia(media) : null), [media])
  const [matches, setMatches] = useState(query?.matches ?? false)
  useEffect(() => {
    if (!query) return
    const update = () => setMatches(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [query])
  return matches
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
  // The article open in the side panel; always the selected one while open.
  const [readingId, setReadingId] = useState<string | null>(null)
  const [view, setView] = useState<View>('galaxy')
  const [fullscreen, setFullscreen] = useState(false)
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)')
  // Phones in portrait get a tall galaxy instead of a wide one, so the planets stay readable.
  const narrow = useMediaQuery('(max-width: 40rem)')

  useEffect(() => {
    let current = true
    fetchGraph(filter).then(
      (value) => {
        if (!current) return
        setGraph(value)
        const keep = (id: string | null) => (id && value.nodes.some((n) => n.id === id) ? id : null)
        setSelectedId(keep)
        setReadingId(keep)
      },
      (e: Error) => current && setError(e.message),
    )
    return () => {
      current = false
    }
  }, [filter])

  const layout = useLayout(graph, !reducedMotion, narrow ? 0.45 : 1)
  const selected = graph?.nodes.find((n) => n.id === selectedId) ?? null
  const reading = graph?.nodes.find((n) => n.id === readingId) ?? null

  /** Selecting another article while one is open shows that one in the side panel; selecting nothing closes it. */
  const select = useCallback((id: string | null) => {
    setSelectedId(id)
    setReadingId((open) => (open && id ? id : null))
  }, [])

  const read = useCallback((id: string) => {
    setSelectedId(id)
    setReadingId(id)
  }, [])

  /** Esc: first closes the article, then clears the selection. */
  const dismiss = useCallback(() => {
    if (readingId) setReadingId(null)
    else setSelectedId(null)
  }, [readingId])

  const inFullscreen = fullscreen && view === 'galaxy'
  const reader = graph && reading && (
    <GalaxyReader
      key={reading.id}
      graph={graph}
      node={reading}
      onSelect={select}
      onClose={() => setReadingId(null)}
      onOpenArticle={(id) => {
        if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
        onOpenArticle(id)
      }}
    />
  )

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
            <select value={selectedId ?? ''} onChange={(event) => select(event.target.value || null)}>
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
        <div className={reader && !inFullscreen ? 'galaxy-layout reading' : 'galaxy-layout'}>
          {view === 'galaxy' ? (
            <GalaxyCanvas
              graph={graph}
              positions={layout.positions}
              settled={layout.done}
              selectedId={selectedId}
              readingId={readingId}
              reducedMotion={reducedMotion}
              fullscreen={fullscreen}
              onFullscreen={setFullscreen}
              onSelect={select}
              onRead={read}
              onDismiss={dismiss}
              reader={inFullscreen ? reader : null}
            />
          ) : (
            <GalaxyList graph={graph} selectedId={selectedId} onSelect={select} />
          )}
          <aside className="project-side">
            {(!inFullscreen && reader) || (
              <>
                <GalaxyDetails graph={graph} node={selected} onSelect={select} onRead={read} />
                <Legend graph={graph} />
              </>
            )}
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
  readingId: string | null
  reducedMotion: boolean
  fullscreen: boolean
  onFullscreen: (on: boolean) => void
  onSelect: (id: string | null) => void
  onRead: (id: string) => void
  onDismiss: () => void
  /** The open article, shown over the galaxy in full screen. */
  reader: ReactNode
}

function GalaxyCanvas({ graph, positions, settled, selectedId, readingId, reducedMotion, fullscreen, onFullscreen, onSelect, onRead, onDismiss, reader }: CanvasProps) {
  const wrapper = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const transform = useRef<Transform>(identity)
  const userMoved = useRef(false)
  const drag = useRef<{ x: number; y: number; start: Transform; moved: boolean } | null>(null)
  const pointers = useRef(new Map<number, { x: number; y: number }>())
  const pinch = useRef<{ distance: number; mid: { x: number; y: number }; start: Transform } | null>(null)
  const animation = useRef<number | null>(null)
  /** Where a running glide is heading. */
  const goal = useRef<Transform | null>(null)
  const frame = useRef<number | null>(null)
  const lastClick = useRef<{ id: string; at: number; x: number; y: number } | null>(null)
  const [width, setWidth] = useState(800)
  const [viewportHeight, setViewportHeight] = useState(HEIGHT)
  const height = fullscreen ? viewportHeight : HEIGHT
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
      drawSpace(ctx, space, width, height, time, t)
      ctx.setTransform(dpr * t.k, 0, 0, dpr * t.k, dpr * t.x, dpr * t.y)

      const focus = selectedId ?? hoverId
      const at = (id: string) => {
        const p = positions[id]
        if (!p) return null
        const offset = float(id, time)
        return { x: p.x + offset.x, y: p.y + offset.y }
      }

      // Relations: softly curved threads of light. The ones of the selected article shine bright, the others fade.
      const glow = graph.nodes.length <= 150
      const selectedFocus = focus !== null && focus === selectedId
      const curve = (a: { x: number; y: number }, b: { x: number; y: number }) => {
        const bend = 0.12
        return { x: (a.x + b.x) / 2 - (b.y - a.y) * bend, y: (a.y + b.y) / 2 + (b.x - a.x) * bend }
      }
      for (const highlighted of [false, true]) {
        if (highlighted && !focus) continue
        ctx.lineWidth = (highlighted ? (selectedFocus ? 3.5 : 2.5) : 1.4) / t.k
        ctx.strokeStyle = highlighted ? 'rgba(251, 207, 232, 0.95)' : focus ? 'rgba(244, 170, 255, 0.08)' : 'rgba(244, 170, 255, 0.4)'
        ctx.shadowColor = 'rgba(236, 72, 153, 0.9)'
        ctx.shadowBlur = highlighted && glow ? (selectedFocus ? 18 : 12) : 0
        ctx.beginPath()
        for (const edge of graph.edges) {
          const a = at(edge.source)
          const b = at(edge.target)
          if (!a || !b || (edge.source === focus || edge.target === focus) !== highlighted) continue
          const c = curve(a, b)
          ctx.moveTo(a.x, a.y)
          ctx.quadraticCurveTo(c.x, c.y, b.x, b.y)
        }
        ctx.stroke()
      }
      // Sparks travel along the selected article's relations, from source to target.
      if (selectedFocus && time > 0) {
        ctx.fillStyle = '#ffffff'
        ctx.shadowColor = 'rgba(251, 207, 232, 1)'
        ctx.shadowBlur = glow ? 10 : 0
        ctx.beginPath()
        for (const edge of graph.edges) {
          if (edge.source !== focus && edge.target !== focus) continue
          const a = at(edge.source)
          const b = at(edge.target)
          if (!a || !b) continue
          const c = curve(a, b)
          for (const phase of [0, 0.5]) {
            const s = (time / 1600 + phase) % 1
            const x = (1 - s) ** 2 * a.x + 2 * (1 - s) * s * c.x + s ** 2 * b.x
            const y = (1 - s) ** 2 * a.y + 2 * (1 - s) * s * c.y + s ** 2 * b.y
            ctx.moveTo(x + 3 / t.k, y)
            ctx.arc(x, y, 3 / t.k, 0, Math.PI * 2)
          }
        }
        ctx.fill()
      }
      ctx.shadowBlur = 0

      // Planets in the color of their type, the title inside.
      const showAllLabels = graph.nodes.length <= 40 || t.k >= 1.6
      for (const node of graph.nodes) {
        const p = at(node.id)
        if (!p) continue
        const r = nodeRadius(node)
        const sx = p.x * t.k + t.x
        const sy = p.y * t.k + t.y
        const sr = r * t.k
        if (sx < -sr - 200 || sx > width + sr || sy < -sr || sy > height + sr) continue
        const color = typeColors[node.articleType]
        const dimmed = neighborIds !== null && !neighborIds.has(node.id)
        ctx.globalAlpha = dimmed ? (selectedFocus ? 0.18 : 0.25) : 1

        drawPlanet(ctx, node.id, p.x, p.y, r, color, {
          detailed: sr >= 14 && glow,
          time,
          ring: node.degree >= 3,
          moon: node.degree >= 4,
        })

        if (node.id === hoverId || node.id === selectedId) {
          ctx.lineWidth = (node.id === selectedId ? 3 : 1.5) / t.k
          ctx.strokeStyle = '#ffffff'
          ctx.beginPath()
          ctx.arc(p.x, p.y, r + 5 / t.k, 0, Math.PI * 2)
          ctx.stroke()
        } else if (selectedFocus && neighborIds?.has(node.id)) {
          // Connected planets get a glowing halo in the color of the threads.
          ctx.lineWidth = 2 / t.k
          ctx.strokeStyle = 'rgba(251, 207, 232, 0.9)'
          ctx.shadowColor = 'rgba(236, 72, 153, 0.9)'
          ctx.shadowBlur = glow ? 10 : 0
          ctx.setLineDash([6 / t.k, 5 / t.k])
          ctx.beginPath()
          ctx.arc(p.x, p.y, r + 6 / t.k, 0, Math.PI * 2)
          ctx.stroke()
          ctx.setLineDash([])
          ctx.shadowBlur = 0
        }

        if (dimmed) continue
        if (sr >= 22) {
          const px = Math.min(15, Math.max(8, sr * 0.2))
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
          ctx.shadowColor = 'rgba(18, 5, 42, 0.95)'
          ctx.shadowBlur = 6
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
    [graph, positions, width, height, neighborIds, selectedId, hoverId, space],
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
      goal.current = target
      const from = transform.current
      const start = performance.now()
      const step = (now: number) => {
        const progress = Math.min(1, (now - start) / 450)
        setTransform(interpolate(from, target, progress))
        animation.current = progress < 1 ? requestAnimationFrame(step) : null
        if (progress === 1) {
          goal.current = null
          if (element) element.dataset.view = 'idle'
        }
      }
      animation.current = requestAnimationFrame(step)
    },
    [reducedMotion, setTransform],
  )

  const fit = useCallback(() => setTransform(fitTransform(Object.values(positions), width, height, FIT_PADDING)), [positions, width, height, setTransform])

  // Follow the settling layout until the person moves the view themselves.
  useEffect(() => {
    if (!userMoved.current && !selectedId) fit()
  }, [fit, selectedId])

  // Focus the selected article.
  useEffect(() => {
    const p = selectedId ? positions[selectedId] : null
    if (p && settled) animateTo(focusTransform(p, width, height, Math.max((goal.current ?? transform.current).k, 1.2)))
  }, [selectedId, settled, positions, width, height, animateTo])

  useEffect(() => {
    const element = wrapper.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => {
      setWidth(Math.max(320, Math.floor(entry.contentRect.width)))
      setViewportHeight(Math.max(320, Math.floor(entry.contentRect.height)))
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  /** The person takes over the view: a running glide stops. */
  const takeOver = useCallback(() => {
    userMoved.current = true
    if (animation.current !== null) {
      cancelAnimationFrame(animation.current)
      animation.current = null
      goal.current = null
      if (canvas.current) canvas.current.dataset.view = 'idle'
    }
  }, [])

  /**
   * Zooming in on a planet until it fills a good part of the view opens its article beside the galaxy.
   * <paramref name="next"/> is the view after the zoom, (sx, sy) the screen point zoomed at.
   */
  const openWhenZoomedIn = useRef<(next: Transform, sx: number, sy: number) => void>(() => {})
  useEffect(() => {
    openWhenZoomedIn.current = (next, sx, sy) => {
      const node = nodeAt(graph.nodes, positions, next, sx, sy)
      if (node && node.id !== readingId && nodeRadius(node) * next.k >= Math.min(OPEN_RADIUS, 0.3 * Math.min(width, height))) onRead(node.id)
    }
  })

  /** Zooms by <paramref name="factor"/> around a screen point, as the wheel, a pinch or the buttons do. */
  const zoomBy = useCallback(
    (from: Transform, factor: number, sx: number, sy: number, animate = false) => {
      const next = zoomAt(from, factor, sx, sy)
      if (animate) animateTo(next)
      else setTransform(next)
      if (factor > 1) openWhenZoomedIn.current(next, sx, sy)
      return next
    },
    [animateTo, setTransform],
  )

  useEffect(() => {
    const element = canvas.current
    if (!element) return
    const wheel = (event: WheelEvent) => {
      event.preventDefault()
      takeOver()
      const rect = element.getBoundingClientRect()
      zoomBy(transform.current, Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top)
    }
    element.addEventListener('wheel', wheel, { passive: false })
    return () => element.removeEventListener('wheel', wheel)
  }, [takeOver, zoomBy])

  function point(event: PointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  function pointerDown(event: PointerEvent<HTMLCanvasElement>) {
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId)
    } catch {
      // The pointer is already gone (or synthetic); panning still works without capture.
    }
    const p = point(event)
    pointers.current.set(event.pointerId, p)
    if (pointers.current.size === 2) {
      // Two fingers: pinch to zoom around their midpoint, move them to pan.
      const [a, b] = [...pointers.current.values()]
      pinch.current = { distance: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)), mid: { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }, start: transform.current }
      drag.current = null
      return
    }
    drag.current = { ...p, start: transform.current, moved: false }
  }

  function pointerMove(event: PointerEvent<HTMLCanvasElement>) {
    const p = point(event)
    if (pointers.current.has(event.pointerId)) pointers.current.set(event.pointerId, p)
    const zoom = pinch.current
    if (zoom && pointers.current.size >= 2) {
      const [a, b] = [...pointers.current.values()]
      const mid = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }
      const factor = Math.hypot(a.x - b.x, a.y - b.y) / zoom.distance
      const zoomed = zoomAt(zoom.start, factor, zoom.mid.x, zoom.mid.y)
      takeOver()
      const next = { ...zoomed, x: zoomed.x + mid.x - zoom.mid.x, y: zoomed.y + mid.y - zoom.mid.y }
      setTransform(next)
      if (factor > 1) openWhenZoomedIn.current(next, mid.x, mid.y)
      return
    }
    const current = drag.current
    if (current) {
      const dx = p.x - current.x
      const dy = p.y - current.y
      if (current.moved || Math.hypot(dx, dy) > 4) {
        current.moved = true
        takeOver()
        setTransform({ ...current.start, x: current.start.x + dx, y: current.start.y + dy })
      }
      return
    }
    const node = nodeAt(graph.nodes, positions, transform.current, p.x, p.y)
    setHoverId(node?.id ?? null)
  }

  function pointerUp(event: PointerEvent<HTMLCanvasElement>) {
    pointers.current.delete(event.pointerId)
    if (pinch.current) {
      // A pinch never selects; the remaining finger does not start a drag either.
      if (pointers.current.size === 0) pinch.current = null
      drag.current = null
      return
    }
    const current = drag.current
    drag.current = null
    if (current && !current.moved) {
      // A second click on the same spot opens the planet of the first one: the view glides towards it in between.
      const p = point(event)
      const previous = lastClick.current
      if (previous && event.timeStamp - previous.at < DOUBLE_CLICK_MS && Math.hypot(p.x - previous.x, p.y - previous.y) < 12) {
        lastClick.current = null
        onRead(previous.id)
        return
      }
      const id = nodeAt(graph.nodes, positions, transform.current, p.x, p.y)?.id ?? null
      lastClick.current = id ? { id, at: event.timeStamp, ...p } : null
      onSelect(id)
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
      '-': () => zoomAt(t, 0.8, width / 2, height / 2),
    }
    if (event.key === '+') {
      event.preventDefault()
      takeOver()
      zoomBy(t, 1.25, width / 2, height / 2)
    } else if (event.key in moves) {
      event.preventDefault()
      takeOver()
      setTransform(moves[event.key]())
    } else if (event.key === '0') {
      event.preventDefault()
      fit()
    } else if (event.key === 'Escape') {
      onDismiss()
    } else if (event.key === 'Enter' && selectedId) {
      event.preventDefault()
      onRead(selectedId)
    }
  }

  /**
   * Full screen: the browser's Fullscreen API where allowed, otherwise the galaxy covers the window.
   * Either way Esc leaves it.
   */
  async function toggleFullscreen() {
    userMoved.current = false
    if (fullscreen) {
      onFullscreen(false)
      if (document.fullscreenElement) await document.exitFullscreen().catch(() => {})
      return
    }
    onFullscreen(true)
    await wrapper.current?.requestFullscreen?.().catch(() => {})
    canvas.current?.focus()
  }

  useEffect(() => {
    if (!fullscreen) return
    const change = () => {
      if (!document.fullscreenElement) onFullscreen(false)
    }
    const escape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape' && !document.fullscreenElement) onFullscreen(false)
    }
    document.addEventListener('fullscreenchange', change)
    window.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('fullscreenchange', change)
      window.removeEventListener('keydown', escape)
    }
  }, [fullscreen, onFullscreen])

  const hovered = hoverId ? graph.nodes.find((n) => n.id === hoverId) : null
  const selected = fullscreen && selectedId ? graph.nodes.find((n) => n.id === selectedId) : null
  const dpr = typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1

  return (
    <div className={fullscreen ? 'galaxy-canvas fullscreen' : 'galaxy-canvas'} ref={wrapper}>
      <canvas
        ref={canvas}
        width={width * dpr}
        height={height * dpr}
        style={{ width, height }}
        tabIndex={0}
        role="img"
        aria-label={`Wissensgalaxie mit ${graph.nodes.length} ${graph.nodes.length === 1 ? 'Artikel' : 'Artikeln'} und ${graph.edges.length} ${graph.edges.length === 1 ? 'Beziehung' : 'Beziehungen'}`}
        aria-describedby="galaxy-help"
        className={hovered ? 'pointer' : undefined}
        data-layout={settled ? 'done' : 'running'}
        onMouseDown={(event) => {
          // The middle mouse button pans like the left one instead of starting the browser's autoscroll.
          if (event.button === 1) event.preventDefault()
        }}
        onPointerDown={pointerDown}
        onPointerMove={pointerMove}
        onPointerUp={pointerUp}
        onPointerCancel={pointerUp}
        onPointerLeave={() => setHoverId(null)}
        onKeyDown={keyDown}
      />
      {hovered && (
        <div className="galaxy-tooltip" aria-hidden="true">
          <strong>{hovered.title}</strong> · {articleTypes[hovered.articleType]}
        </div>
      )}
      <div className="galaxy-controls">
        <button type="button" aria-label="Vergrößern" onClick={() => zoomBy(transform.current, 1.4, width / 2, height / 2, true)}>
          +
        </button>
        <button type="button" aria-label="Verkleinern" onClick={() => animateTo(zoomAt(transform.current, 1 / 1.4, width / 2, height / 2))}>
          −
        </button>
        <button
          type="button"
          onClick={() => {
            userMoved.current = false
            animateTo(fitTransform(Object.values(positions), width, height, FIT_PADDING))
          }}
        >
          Alles zeigen
        </button>
        <button type="button" aria-pressed={fullscreen} onClick={toggleFullscreen}>
          {fullscreen ? 'Vollbild beenden' : 'Vollbild'}
        </button>
      </div>
      {reader ? (
        <div className="galaxy-overlay-reader">{reader}</div>
      ) : selected && (
        <div className="galaxy-card" aria-live="polite">
          <span className="galaxy-card-type">
            <span className="legend-dot" style={{ background: typeColors[selected.articleType] }} aria-hidden="true" /> {articleTypes[selected.articleType]}
          </span>
          <strong>{selected.title}</strong>
          {selected.summary && <p>{selected.summary}</p>}
          <button type="button" onClick={() => onRead(selected.id)}>
            Artikel öffnen
          </button>
        </div>
      )}
      <p id="galaxy-help" className="muted">
        Ziehen (auch mit dem mittleren Mausrad) verschiebt, Mausrad oder zwei Finger zoomen. Klick oder Tippen wählt einen Artikel und hebt seine Verbindungen hervor; Doppelklick
        oder Hineinzoomen öffnet ihn an der Seite. Tastatur: Pfeiltasten, + und −, 0 zeigt alles, Enter öffnet den gewählten Artikel, Esc schließt ihn bzw. hebt die Auswahl auf.
        Die Darstellung „Liste“ zeigt dieselben Inhalte.
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

type DetailsProps = { graph: KnowledgeGraph; node: GraphNode | null; onSelect: (id: string) => void; onRead: (id: string) => void }

function GalaxyDetails({ graph, node, onSelect, onRead }: DetailsProps) {
  if (!node) {
    return (
      <section className="panel" aria-labelledby="galaxy-details-heading">
        <h3 id="galaxy-details-heading">Auswahl</h3>
        <p className="muted">
          {graph.nodes.length} Artikel, {graph.edges.length} Beziehungen. Wähle einen Artikel, um seine Beziehungen zu sehen; Doppelklick öffnet ihn.
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
      <button type="button" onClick={() => onRead(node.id)}>
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
