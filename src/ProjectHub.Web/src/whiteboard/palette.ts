import type { Color } from './model'

/** Fill, outline and text color per object color; shared by the canvas, the sticky stack and the template previews. */
export const fills: Record<Color, string> = {
  white: '#ffffff',
  yellow: '#fff3a3',
  orange: '#ffd8a8',
  red: '#ffc9c9',
  pink: '#ffd6e7',
  violet: '#e5dbff',
  blue: '#cfe0ff',
  cyan: '#c5f6fa',
  teal: '#c3fae8',
  green: '#c9f0d3',
  lime: '#e3f5b8',
  sand: '#f1e4d1',
  gray: '#e3e7ec',
  navy: '#2b3f8f',
  purple: '#5a3aa6',
  black: '#262b33',
}

export const strokes: Record<Color, string> = {
  white: '#8a95a3',
  yellow: '#b59a00',
  orange: '#c2610c',
  red: '#c92a2a',
  pink: '#c2417a',
  violet: '#6741d9',
  blue: '#2a5bd7',
  cyan: '#0c8599',
  teal: '#099268',
  green: '#2f8a4c',
  lime: '#5c940d',
  sand: '#8c6a3f',
  gray: '#5a6573',
  navy: '#1c2b66',
  purple: '#3d2675',
  black: '#0d0f12',
}

const darkColors = new Set<Color>(['navy', 'purple', 'black'])

/** Text on a filled shape: dark on the light colors, white on the three dark ones. */
export const textColor = (color: Color) => (darkColors.has(color) ? '#ffffff' : '#1f2937')

/** Corner points of a diamond (flowchart decision) in the given box. */
export const diamondPoints = (x: number, y: number, w: number, h: number) =>
  `${x + w / 2},${y} ${x + w},${y + h / 2} ${x + w / 2},${y + h} ${x},${y + h / 2}`
