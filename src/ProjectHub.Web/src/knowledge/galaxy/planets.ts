/**
 * Drawing of one article as a planet: lit sphere with atmosphere, drifting surface clouds,
 * a ring for well-connected articles and a moon for hubs. World coordinates; decoration only.
 */

type Rgb = { r: number; g: number; b: number }

const parse = (hex: string): Rgb => ({ r: parseInt(hex.slice(1, 3), 16), g: parseInt(hex.slice(3, 5), 16), b: parseInt(hex.slice(5, 7), 16) })

const mix = (a: Rgb, b: Rgb, t: number): Rgb => ({ r: a.r + (b.r - a.r) * t, g: a.g + (b.g - a.g) * t, b: a.b + (b.b - a.b) * t })

const rgba = (c: Rgb, alpha = 1) => `rgba(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)}, ${alpha})`

const WHITE: Rgb = { r: 255, g: 255, b: 255 }
const SPACE: Rgb = { r: 18, g: 5, b: 42 }

/** Stable pseudo-random numbers per article, so each planet keeps its own look. */
export function seedOf(id: string): number {
  let hash = 2166136261
  for (let i = 0; i < id.length; i++) hash = Math.imul(hash ^ id.charCodeAt(i), 16777619)
  return (hash >>> 0) / 4294967296
}

export type PlanetStyle = {
  /** Draw surface, ring and moon; off for tiny planets on screen. */
  detailed: boolean
  /** Time in ms for rotation and orbits; 0 = still. */
  time: number
  /** Ring for articles with several relations, a moon for hubs. */
  ring: boolean
  moon: boolean
}

export function drawPlanet(ctx: CanvasRenderingContext2D, id: string, x: number, y: number, r: number, hex: string, style: PlanetStyle) {
  const color = parse(hex)
  const seed = seedOf(id)
  const tilt = -0.35 + seed * 0.3
  const moonAngle = seed * Math.PI * 2 + style.time / (2600 + seed * 1800)
  const moonBehind = Math.sin(moonAngle) < 0

  // Atmosphere: a soft halo in the planet's color.
  const halo = ctx.createRadialGradient(x, y, r * 0.85, x, y, r * 1.45)
  halo.addColorStop(0, rgba(color, 0.35))
  halo.addColorStop(1, rgba(color, 0))
  ctx.fillStyle = halo
  ctx.beginPath()
  ctx.arc(x, y, r * 1.45, 0, Math.PI * 2)
  ctx.fill()

  if (style.detailed && style.ring) drawRing(ctx, x, y, r, color, tilt, 'back')
  if (style.detailed && style.moon && moonBehind) drawMoon(ctx, x, y, r, moonAngle, tilt)

  ctx.save()
  ctx.beginPath()
  ctx.arc(x, y, r, 0, Math.PI * 2)
  ctx.clip()

  // Body, lit from the upper left.
  const body = ctx.createRadialGradient(x - r * 0.4, y - r * 0.45, r * 0.05, x, y, r * 1.1)
  body.addColorStop(0, rgba(mix(color, WHITE, 0.45)))
  body.addColorStop(0.45, rgba(color))
  body.addColorStop(1, rgba(mix(color, SPACE, 0.7)))
  ctx.fillStyle = body
  ctx.fillRect(x - r, y - r, r * 2, r * 2)

  if (style.detailed) {
    // Soft cloud bands across the whole disc, and a storm that drifts by as the planet turns.
    ctx.save()
    ctx.translate(x, y)
    ctx.rotate(tilt * 0.5)
    for (let i = 0; i < 5; i++) {
      const band = seedOf(`${id}:${i}`)
      const by = (-0.75 + i * 0.36 + band * 0.1) * r
      const height = (0.06 + band * 0.08) * r
      const tone = i % 2 === 0 ? rgba(WHITE, 0.08 + band * 0.07) : rgba(SPACE, 0.1 + band * 0.1)
      const gradient = ctx.createLinearGradient(0, by - height, 0, by + height)
      gradient.addColorStop(0, 'rgba(0, 0, 0, 0)')
      gradient.addColorStop(0.5, tone)
      gradient.addColorStop(1, 'rgba(0, 0, 0, 0)')
      ctx.fillStyle = gradient
      ctx.fillRect(-r * 1.2, by - height, r * 2.4, height * 2)
    }
    const storm = seedOf(`${id}:storm`)
    const sx = -r * 1.3 + ((style.time / (14000 + storm * 8000) + storm) % 1) * r * 2.6
    const sy = (storm - 0.5) * r * 0.9
    ctx.fillStyle = rgba(mix(color, WHITE, 0.35), 0.35)
    ctx.beginPath()
    ctx.ellipse(sx, sy, r * 0.17, r * 0.07, 0, 0, Math.PI * 2)
    ctx.fill()
    ctx.restore()
  }

  // Night side.
  const shade = ctx.createLinearGradient(x - r * 0.6, y - r * 0.6, x + r, y + r)
  shade.addColorStop(0, 'rgba(18, 5, 42, 0)')
  shade.addColorStop(0.55, 'rgba(18, 5, 42, 0.15)')
  shade.addColorStop(1, 'rgba(18, 5, 42, 0.7)')
  ctx.fillStyle = shade
  ctx.fillRect(x - r, y - r, r * 2, r * 2)
  ctx.restore()

  // Thin rim light where the sun hits the edge.
  ctx.strokeStyle = rgba(mix(color, WHITE, 0.6), 0.28)
  ctx.lineWidth = r * 0.03
  ctx.beginPath()
  ctx.arc(x, y, r * 0.985, Math.PI * 0.9, Math.PI * 1.65)
  ctx.stroke()

  if (style.detailed && style.ring) drawRing(ctx, x, y, r, color, tilt, 'front')
  if (style.detailed && style.moon && !moonBehind) drawMoon(ctx, x, y, r, moonAngle, tilt)
}

function drawRing(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, color: Rgb, tilt: number, half: 'back' | 'front') {
  ctx.save()
  ctx.translate(x, y)
  ctx.rotate(tilt)
  const [start, end] = half === 'back' ? [Math.PI, Math.PI * 2] : [0, Math.PI]
  for (const [scale, width, alpha] of [
    [1.75, 0.1, 0.45],
    [1.5, 0.05, 0.3],
  ] as const) {
    ctx.strokeStyle = rgba(mix(color, WHITE, 0.4), alpha)
    ctx.lineWidth = r * width
    ctx.beginPath()
    ctx.ellipse(0, 0, r * scale, r * scale * 0.24, 0, start, end)
    ctx.stroke()
  }
  ctx.restore()
}

function drawMoon(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, angle: number, tilt: number) {
  const ox = Math.cos(angle) * r * 1.9
  const oy = Math.sin(angle) * r * 0.5
  const mx = x + ox * Math.cos(tilt) - oy * Math.sin(tilt)
  const my = y + ox * Math.sin(tilt) + oy * Math.cos(tilt)
  const mr = r * 0.16
  const moon = ctx.createRadialGradient(mx - mr * 0.4, my - mr * 0.4, mr * 0.1, mx, my, mr)
  moon.addColorStop(0, '#f4f0ff')
  moon.addColorStop(1, '#6b6390')
  ctx.fillStyle = moon
  ctx.beginPath()
  ctx.arc(mx, my, mr, 0, Math.PI * 2)
  ctx.fill()
}
