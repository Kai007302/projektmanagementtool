import { describe, expect, it } from 'vitest'
import { calendarFileName, nextDay, outlookComposeUrl } from './calendar'

describe('calendar', () => {
  it('ends all-day entries on the following day', () => {
    expect(nextDay('2026-02-28')).toBe('2026-03-01')
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
  })

  it('builds an Outlook compose link with title, dates and app link', () => {
    const url = new URL(outlookComposeUrl('Texte & Bilder', '2026-11-02', '2026-11-04', 'https://projecthub.example'))

    expect(url.origin).toBe('https://outlook.office.com')
    expect(url.searchParams.get('subject')).toBe('Texte & Bilder')
    expect(url.searchParams.get('startdt')).toBe('2026-11-02')
    expect(url.searchParams.get('enddt')).toBe('2026-11-05')
    expect(url.searchParams.get('allday')).toBe('true')
    expect(url.searchParams.get('body')).toBe('In ProjectHub öffnen: https://projecthub.example')
  })

  it('treats a last day before the first as a single day', () => {
    const url = new URL(outlookComposeUrl('x', '2026-03-10', '2026-03-01', 'https://app'))
    expect(url.searchParams.get('enddt')).toBe('2026-03-11')
  })

  it('makes safe file names', () => {
    expect(calendarFileName('Texte schreiben')).toBe('Texte schreiben.ics')
    expect(calendarFileName('../a:b')).toBe('.. a b.ics')
    expect(calendarFileName('  ')).toBe('termin.ics')
  })
})
