import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { CalendarFeedPanel } from './CalendarFeedPanel'
import { webcalUrl } from './calendar'

const url = 'https://projecthub.example/api/v1/calendar-feed.ics?token=abc'

describe('CalendarFeedPanel', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('creates the address once and shows it with copy and subscribe links', async () => {
    const api = fakeApi({
      'GET /api/v1/me/calendar-feed': () => json({ active: false, createdAt: null, lastUsedAt: null }),
      'POST /api/v1/me/calendar-feed': () => json({ url, createdAt: '2026-10-05T08:00:00Z' }),
    })
    const writeText = vi.fn(() => Promise.resolve())
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    render(<CalendarFeedPanel />)

    await userEvent.click(await screen.findByRole('button', { name: 'Kalender-Adresse erstellen' }))

    expect(screen.getByLabelText('Deine Kalender-Adresse')).toHaveValue(url)
    expect(screen.getByRole('link', { name: 'In Kalender-App öffnen' })).toHaveAttribute('href', 'webcal://projecthub.example/api/v1/calendar-feed.ics?token=abc')
    await userEvent.click(screen.getByRole('button', { name: 'Kopieren' }))
    expect(writeText).toHaveBeenCalledWith(url)
    expect(screen.getByRole('button', { name: 'Kopiert ✓' })).toBeInTheDocument()
    expect(api.calls.filter((c) => c.key === 'POST /api/v1/me/calendar-feed')).toHaveLength(1)
  })

  it('shows an existing address without revealing it and deletes it after confirmation', async () => {
    const api = fakeApi({
      'GET /api/v1/me/calendar-feed': () => json({ active: true, createdAt: '2026-10-01T08:00:00Z', lastUsedAt: null }),
      'DELETE /api/v1/me/calendar-feed': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<CalendarFeedPanel />)

    expect(await screen.findByText(/Adresse aktiv seit .*noch nicht abgerufen/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Deine Kalender-Adresse')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Adresse löschen' }))

    expect(api.calls.some((c) => c.key === 'DELETE /api/v1/me/calendar-feed')).toBe(true)
    expect(await screen.findByRole('button', { name: 'Kalender-Adresse erstellen' })).toBeInTheDocument()
  })

  it('turns the address into a webcal link', () => {
    expect(webcalUrl('http://localhost:5173/api/v1/calendar-feed.ics?token=x')).toBe('webcal://localhost:5173/api/v1/calendar-feed.ics?token=x')
  })
})
