/**
 * The space behind the galaxy: a violet-pink nebula, twinkling stars and a slowly drifting mesh.
 * Everything here is decoration in screen coordinates; it carries no data.
 */

type Star = { x: number; y: number; r: number; phase: number }
type MeshPoint = { x: number; y: number; vx: number; vy: number }

export type Space = { stars: Star[]; mesh: MeshPoint[] }

/** Small deterministic generator, so the sky looks the same on every render. */
function random(seed: number) {
  let s = seed
  return () => {
    s = (s * 1664525 + 1013904223) % 4294967296
    return s / 4294967296
  }
}

export function createSpace(): Space {
  const next = random(42)
  return {
    stars: Array.from({ length: 160 }, () => ({ x: next(), y: next(), r: 0.4 + next() * 1.1, phase: next() * Math.PI * 2 })),
    mesh: Array.from({ length: 56 }, () => ({ x: next(), y: next(), vx: (next() - 0.5) * 0.000012, vy: (next() - 0.5) * 0.000012 })),
  }
}

const MESH_DISTANCE = 150

/**
 * Paints the background. <paramref name="time"/> is in milliseconds (0 = frozen, for reduced motion);
 * <paramref name="pan"/> shifts the mesh a little for a sense of depth.
 */
export function drawSpace(ctx: CanvasRenderingContext2D, space: Space, width: number, height: number, time: number, pan: { x: number; y: number }) {
  const base = ctx.createLinearGradient(0, 0, width, height)
  base.addColorStop(0, '#12052a')
  base.addColorStop(0.55, '#24093f')
  base.addColorStop(1, '#3a0c4a')
  ctx.fillStyle = base
  ctx.fillRect(0, 0, width, height)

  for (const [cx, cy, radius, color] of [
    [0.25, 0.3, 0.55, 'rgba(168, 85, 247, 0.35)'],
    [0.78, 0.7, 0.5, 'rgba(236, 72, 153, 0.28)'],
    [0.6, 0.15, 0.35, 'rgba(99, 102, 241, 0.22)'],
  ] as const) {
    const x = cx * width
    const y = cy * height
    const r = radius * Math.max(width, height)
    const nebula = ctx.createRadialGradient(x, y, 0, x, y, r)
    nebula.addColorStop(0, color)
    nebula.addColorStop(1, 'rgba(0, 0, 0, 0)')
    ctx.fillStyle = nebula
    ctx.fillRect(0, 0, width, height)
  }

  for (const star of space.stars) {
    const twinkle = time === 0 ? 0.7 : 0.45 + 0.4 * Math.sin(time / 900 + star.phase)
    ctx.globalAlpha = twinkle
    ctx.fillStyle = '#ffffff'
    ctx.beginPath()
    ctx.arc(star.x * width, star.y * height, star.r, 0, Math.PI * 2)
    ctx.fill()
  }
  ctx.globalAlpha = 1

  // The mesh drifts and wraps around; points close to each other are connected.
  const shiftX = pan.x * 0.08
  const shiftY = pan.y * 0.08
  const points = space.mesh.map((p) => ({
    x: wrap(p.x + p.vx * time + shiftX / width) * width,
    y: wrap(p.y + p.vy * time + shiftY / height) * height,
  }))
  ctx.lineWidth = 0.6
  for (let i = 0; i < points.length; i++) {
    for (let j = i + 1; j < points.length; j++) {
      const d = Math.hypot(points[i].x - points[j].x, points[i].y - points[j].y)
      if (d > MESH_DISTANCE) continue
      ctx.strokeStyle = `rgba(244, 170, 255, ${0.22 * (1 - d / MESH_DISTANCE)})`
      ctx.beginPath()
      ctx.moveTo(points[i].x, points[i].y)
      ctx.lineTo(points[j].x, points[j].y)
      ctx.stroke()
    }
  }
  ctx.fillStyle = 'rgba(244, 170, 255, 0.45)'
  for (const p of points) {
    ctx.beginPath()
    ctx.arc(p.x, p.y, 1.3, 0, Math.PI * 2)
    ctx.fill()
  }
}

const wrap = (value: number) => ((value % 1) + 1) % 1

/** Gentle floating offset of a bubble in world units; 0 when motion is reduced. */
export function float(id: string, time: number): { x: number; y: number } {
  if (time === 0) return { x: 0, y: 0 }
  let hash = 0
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) | 0
  const phase = (hash % 1000) / 159.15
  return { x: Math.sin(time / 2300 + phase) * 4, y: Math.cos(time / 2900 + phase * 1.3) * 5 }
}

/** Splits a title into at most <paramref name="maxLines"/> lines that fit <paramref name="maxWidth"/>. */
export function wrapTitle(ctx: CanvasRenderingContext2D, title: string, maxWidth: number, maxLines = 3): string[] {
  const lines: string[] = []
  let line = ''
  // Break after spaces and after hyphens, so "Projekt-Kickoff" can wrap as "Projekt-" / "Kickoff".
  for (const word of title.split(/\s+/).flatMap((w) => w.split(/(?<=-)/))) {
    const candidate = !line ? word : line.endsWith('-') ? `${line}${word}` : `${line} ${word}`
    if (ctx.measureText(candidate).width <= maxWidth || !line) {
      line = candidate
    } else {
      lines.push(line)
      line = word
    }
  }
  if (line) lines.push(line)
  if (lines.length <= maxLines) return lines.map((l) => ellipsize(ctx, l, maxWidth))
  const kept = lines.slice(0, maxLines)
  kept[maxLines - 1] = ellipsize(ctx, kept[maxLines - 1], maxWidth, true)
  return kept.map((l) => ellipsize(ctx, l, maxWidth))
}

function ellipsize(ctx: CanvasRenderingContext2D, text: string, maxWidth: number, force = false) {
  if (!force && ctx.measureText(text).width <= maxWidth) return text
  let cut = text
  while (cut.length > 1 && ctx.measureText(`${cut}…`).width > maxWidth) cut = cut.slice(0, -1)
  return `${cut.trimEnd()}…`
}
