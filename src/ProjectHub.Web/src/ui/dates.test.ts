import { describe, expect, it } from 'vitest'
import { formatDate, parseDate } from './dates'

// Wednesday, 7 October 2026
const now = new Date(2026, 9, 7, 15, 30)

describe('parseDate', () => {
  it.each([
    ['', ''],
    ['2026-11-30', '2026-11-30'],
    ['30.11.2026', '2026-11-30'],
    ['30.11.26', '2026-11-30'],
    ['30.11.', '2026-11-30'],
    ['1.3', '2027-03-01'],
    ['7.10.', '2026-10-07'],
    ['heute', '2026-10-07'],
    ['Morgen', '2026-10-08'],
    ['übermorgen', '2026-10-09'],
    ['+3', '2026-10-10'],
    ['+ 2w', '2026-10-21'],
    ['-1', '2026-10-06'],
    ['Fr', '2026-10-09'],
    ['freitag', '2026-10-09'],
    ['Mi', '2026-10-14'],
  ])('reads %j as %s', (text, expected) => {
    expect(parseDate(text, now)).toBe(expected)
  })

  it.each(['31.02.2026', 'bald', '13.13.', '2026-02-30'])('refuses %j', (text) => {
    expect(parseDate(text, now)).toBeNull()
  })

  it('shows a day the German way', () => {
    expect(formatDate('2026-11-30')).toBe('30.11.2026')
    expect(formatDate('')).toBe('')
  })
})
