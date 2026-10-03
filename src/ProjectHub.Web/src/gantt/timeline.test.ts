import { describe, expect, it } from 'vitest'
import type { GanttTask } from './api'
import { anchorsOf, dragged, formatDay, headerOf, isoWeek, parseDay, rangeOf, rowsOf, spanOf, today, weekday } from './timeline'

const task = (id: string, parentTaskId: string | null = null, startDate: string | null = null, dueDate: string | null = null): GanttTask => ({
  id,
  parentTaskId,
  title: id,
  status: 'todo',
  assigneeName: null,
  startDate,
  dueDate,
  progress: 0,
  version: 1,
})

describe('timeline', () => {
  it('converts dates to whole days and back', () => {
    const day = parseDay('2026-11-02')
    expect(formatDay(day)).toBe('2026-11-02')
    expect(formatDay(day + 30)).toBe('2026-12-02')
    expect(parseDay('2026-03-30') - parseDay('2026-03-28')).toBe(2)
    expect(today(new Date(2026, 10, 2, 23, 30))).toBe(day)
  })

  it('knows weekdays and ISO weeks', () => {
    expect(weekday(parseDay('2026-11-02'))).toBe(0)
    expect(weekday(parseDay('2026-11-08'))).toBe(6)
    expect(isoWeek(parseDay('2026-11-02'))).toBe(45)
    expect(isoWeek(parseDay('2027-01-01'))).toBe(53)
    expect(isoWeek(parseDay('2026-01-01'))).toBe(1)
  })

  it('gives tasks with one date a single day', () => {
    expect(spanOf(task('a', null, '2026-11-02', null))).toEqual({ start: parseDay('2026-11-02'), end: parseDay('2026-11-02') })
    expect(spanOf(task('a', null, null, '2026-11-04'))?.end).toBe(parseDay('2026-11-04'))
    expect(spanOf(task('a'))).toBeNull()
  })

  it('pads the range and aligns it to the zoom', () => {
    const days = [parseDay('2026-11-04'), parseDay('2026-11-20')]
    const now = parseDay('2026-11-10')
    expect(rangeOf(days, 'day', now)).toEqual({ start: parseDay('2026-11-01'), end: parseDay('2026-11-23') })
    const week = rangeOf(days, 'week', now)
    expect(weekday(week.start)).toBe(0)
    expect(weekday(week.end)).toBe(6)
    expect(rangeOf(days, 'month', now)).toEqual({ start: parseDay('2026-10-01'), end: parseDay('2026-12-31') })
  })

  it('builds header rows per zoom', () => {
    const range = { start: parseDay('2026-10-29'), end: parseDay('2026-11-09') }
    const day = headerOf(range, 'day')
    expect(day.top.map((s) => s.label)).toEqual(['Oktober 2026', 'November 2026'])
    expect(day.top[0].end).toBe(parseDay('2026-11-01'))
    expect(day.bottom).toHaveLength(12)
    const week = headerOf(range, 'week')
    expect(week.bottom.map((s) => s.label)).toEqual(['KW 44', 'KW 45', 'KW 46'])
    expect(week.bottom[2].end).toBe(range.end + 1)
    expect(headerOf(range, 'month').bottom.map((s) => s.label)).toEqual(['Okt', 'Nov'])
  })

  it('orders subtasks below their parent', () => {
    const rows = rowsOf([task('a'), task('b'), task('a1', 'a'), task('a1x', 'a1'), task('orphan', 'gone')])
    expect(rows.map((r) => `${r.task.id}:${r.depth}`)).toEqual(['a:0', 'a1:1', 'a1x:2', 'b:0', 'orphan:0'])
  })

  it('drags bars without making them shorter than a day', () => {
    const span = { start: 10, end: 14 }
    expect(dragged(span, 'move', 3)).toEqual({ start: 13, end: 17 })
    expect(dragged(span, 'end', -2)).toEqual({ start: 10, end: 12 })
    expect(dragged(span, 'end', -9)).toEqual({ start: 10, end: 10 })
    expect(dragged(span, 'start', 9)).toEqual({ start: 14, end: 14 })
  })

  it('anchors dependencies by type', () => {
    expect(anchorsOf('finish_to_start')).toEqual({ source: 'end', target: 'start' })
    expect(anchorsOf('start_to_finish')).toEqual({ source: 'start', target: 'end' })
  })
})
