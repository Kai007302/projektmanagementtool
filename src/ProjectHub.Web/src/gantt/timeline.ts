import type { DependencyType, GanttTask } from './api'

/** Days are whole numbers counted from 1970-01-01, so date math never meets time zones or daylight saving. */
export type Day = number

export type Zoom = 'day' | 'week' | 'month'

export const zoomText: Record<Zoom, string> = { day: 'Tag', week: 'Woche', month: 'Monat' }

/** Width of one day in pixels per zoom level. */
export const dayWidth: Record<Zoom, number> = { day: 32, week: 12, month: 4 }

const msPerDay = 86_400_000

export function parseDay(value: string): Day {
  const [year, month, day] = value.split('-').map(Number)
  return Date.UTC(year, month - 1, day) / msPerDay
}

export function formatDay(day: Day): string {
  return new Date(day * msPerDay).toISOString().slice(0, 10)
}

/** Today in the user's time zone. */
export function today(now = new Date()): Day {
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / msPerDay
}

const utc = (day: Day) => new Date(day * msPerDay)

/** 0 = Monday … 6 = Sunday. */
export const weekday = (day: Day) => (utc(day).getUTCDay() + 6) % 7

export const isWeekend = (day: Day) => weekday(day) >= 5

/** ISO 8601 week number. */
export function isoWeek(day: Day): number {
  const thursday = day - weekday(day) + 3
  const yearStart = Date.UTC(utc(thursday).getUTCFullYear(), 0, 1) / msPerDay
  return Math.floor((thursday - yearStart) / 7) + 1
}

const startOfMonth = (day: Day) => {
  const date = utc(day)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1) / msPerDay
}

const nextMonth = (day: Day) => {
  const date = utc(day)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1) / msPerDay
}

/** The days a task occupies, both inclusive; a task with one date takes a single day. */
export function spanOf(task: Pick<GanttTask, 'startDate' | 'dueDate'>): { start: Day; end: Day } | null {
  const start = task.startDate ?? task.dueDate
  const end = task.dueDate ?? task.startDate
  return start && end ? { start: parseDay(start), end: parseDay(end) } : null
}

export type Range = { start: Day; end: Day }

/** The visible days: everything scheduled plus today, padded and aligned to the zoom. */
export function rangeOf(days: Day[], zoom: Zoom, now: Day): Range {
  const all = [...days, now]
  const padding = { day: 3, week: 7, month: 30 }[zoom]
  let start = Math.min(...all) - padding
  let end = Math.max(...all) + padding
  if (days.length === 0) end = Math.max(end, now + 28)
  if (zoom === 'week') {
    start -= weekday(start)
    end += 6 - weekday(end)
  } else if (zoom === 'month') {
    start = startOfMonth(start)
    end = nextMonth(end) - 1
  }
  return { start, end }
}

export type Segment = { start: Day; end: Day; label: string }

// Fixed names instead of Intl, whose abbreviations differ between browsers and Node.
const monthNames = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember']
const shortMonthNames = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez']

/** Splits the range into consecutive segments; <paramref name="next"/> gives the first day of the following segment. */
function segments(range: Range, next: (day: Day) => Day, label: (day: Day) => string): Segment[] {
  const result: Segment[] = []
  for (let day = range.start; day <= range.end; day = next(day)) {
    result.push({ start: day, end: Math.min(next(day), range.end + 1), label: label(day) })
  }
  return result
}

/** Two header rows: a coarse one (months or years) and a fine one (days, weeks or months). */
export function headerOf(range: Range, zoom: Zoom): { top: Segment[]; bottom: Segment[] } {
  const months = segments(range, nextMonth, (day) => `${monthNames[utc(day).getUTCMonth()]} ${utc(day).getUTCFullYear()}`)
  if (zoom === 'day') {
    return { top: months, bottom: segments(range, (day) => day + 1, (day) => String(utc(day).getUTCDate())) }
  }

  if (zoom === 'week') {
    return { top: months, bottom: segments(range, (day) => day - weekday(day) + 7, (day) => `KW ${isoWeek(day)}`) }
  }

  const nextYear = (day: Day) => Date.UTC(utc(day).getUTCFullYear() + 1, 0, 1) / msPerDay
  return {
    top: segments(range, nextYear, (day) => String(utc(day).getUTCFullYear())),
    bottom: segments(range, nextMonth, (day) => shortMonthNames[utc(day).getUTCMonth()]),
  }
}

export type Row = { task: GanttTask; depth: number }

/** Tasks in outline order: every parent directly followed by its subtasks. */
export function rowsOf(tasks: GanttTask[]): Row[] {
  const ids = new Set(tasks.map((task) => task.id))
  const children = new Map<string | null, GanttTask[]>()
  for (const task of tasks) {
    // A subtask whose parent is not in the list is shown on the top level.
    const parent = task.parentTaskId && ids.has(task.parentTaskId) ? task.parentTaskId : null
    children.set(parent, [...(children.get(parent) ?? []), task])
  }

  const rows: Row[] = []
  const visit = (parent: string | null, depth: number) => {
    for (const task of children.get(parent) ?? []) {
      rows.push({ task, depth })
      visit(task.id, depth + 1)
    }
  }
  visit(null, 0)
  return rows
}

export type DragMode = 'move' | 'start' | 'end'

/** The dates after dragging a bar by <paramref name="delta"/> days; a bar never gets shorter than one day. */
export function dragged(span: { start: Day; end: Day }, mode: DragMode, delta: number): { start: Day; end: Day } {
  if (mode === 'move') return { start: span.start + delta, end: span.end + delta }
  if (mode === 'start') return { start: Math.min(span.start + delta, span.end), end: span.end }
  return { start: span.start, end: Math.max(span.end + delta, span.start) }
}

/** Where a dependency leaves its source and enters its target: at the start or the end of the bar. */
export function anchorsOf(type: DependencyType): { source: 'start' | 'end'; target: 'start' | 'end' } {
  const [from, to] = type.split('_to_')
  return { source: from === 'finish' ? 'end' : 'start', target: to === 'finish' ? 'end' : 'start' }
}

/** For example „2. Nov 2026“. */
export const displayDay = (day: Day) => `${utc(day).getUTCDate()}. ${shortMonthNames[utc(day).getUTCMonth()]} ${utc(day).getUTCFullYear()}`
