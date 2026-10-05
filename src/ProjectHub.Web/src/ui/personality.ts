/** Small touches that give the app character. Purely presentational; nothing here is stored. */

const projectEmojis = ['🚀', '🌱', '🧭', '🛠️', '🎯', '💡', '📦', '🌍', '🧩', '⚡', '🏗️', '🔭']
const projectColors = ['#6366f1', '#0ea5e9', '#10b981', '#f59e0b', '#ef4444', '#ec4899', '#8b5cf6', '#14b8a6']

function hash(text: string) {
  let h = 2166136261
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619)
  return h >>> 0
}

/** A stable emoji and color per project, derived from its id. */
export function projectLook(id: string) {
  const h = hash(id)
  return { emoji: projectEmojis[h % projectEmojis.length], color: projectColors[(h >>> 8) % projectColors.length] }
}

/** "Guten Morgen" / "Guten Tag" / "Guten Abend" by local time. */
export function greeting(date = new Date()) {
  const hour = date.getHours()
  if (hour < 5) return 'Hallo'
  if (hour < 11) return 'Guten Morgen'
  if (hour < 18) return 'Guten Tag'
  return 'Guten Abend'
}

/** Today as YYYY-MM-DD in local time, comparable with task due dates. */
export function today() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

const relative = new Intl.RelativeTimeFormat('de-DE', { numeric: 'auto' })

/** "vor 5 Minuten", "gestern", "vor 3 Tagen" … */
export function relativeTime(iso: string, now = Date.now()) {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000)
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [
    ['second', 60],
    ['minute', 60],
    ['hour', 24],
    ['day', 7],
    ['week', 4.35],
    ['month', 12],
  ]
  let value = seconds
  for (const [unit, size] of steps) {
    if (Math.abs(value) < size) return unit === 'second' ? 'gerade eben' : relative.format(Math.round(value), unit)
    value /= size
  }
  return relative.format(Math.round(value), 'year')
}
