import type { Color } from './model'

/** Fill and outline per object color; shared by the canvas and the template previews. */
export const fills: Record<Color, string> = {
  yellow: '#fff3a3',
  green: '#c9f0d3',
  blue: '#cfe0ff',
  pink: '#ffd6e7',
  gray: '#e3e7ec',
  white: '#ffffff',
}

export const strokes: Record<Color, string> = {
  yellow: '#b59a00',
  green: '#2f8a4c',
  blue: '#2a5bd7',
  pink: '#c2417a',
  gray: '#5a6573',
  white: '#8a95a3',
}

/** Corner points of a diamond (flowchart decision) in the given box. */
export const diamondPoints = (x: number, y: number, w: number, h: number) =>
  `${x + w / 2},${y} ${x + w},${y + h / 2} ${x + w / 2},${y + h} ${x},${y + h / 2}`
