import { describe, expect, it } from 'vitest'
import { greeting, projectLook, relativeTime } from './personality'

describe('personality', () => {
  it('greets by time of day', () => {
    expect(greeting(new Date(2026, 9, 4, 8))).toBe('Guten Morgen')
    expect(greeting(new Date(2026, 9, 4, 13))).toBe('Guten Tag')
    expect(greeting(new Date(2026, 9, 4, 21))).toBe('Guten Abend')
  })

  it('gives a project the same look every time', () => {
    expect(projectLook('0192-abc')).toEqual(projectLook('0192-abc'))
  })

  it('formats times relative to now', () => {
    const now = Date.parse('2026-10-04T12:00:00Z')
    expect(relativeTime('2026-10-04T11:59:40Z', now)).toBe('gerade eben')
    expect(relativeTime('2026-10-04T11:55:00Z', now)).toBe('vor 5 Minuten')
    expect(relativeTime('2026-10-03T12:00:00Z', now)).toBe('gestern')
  })
})
