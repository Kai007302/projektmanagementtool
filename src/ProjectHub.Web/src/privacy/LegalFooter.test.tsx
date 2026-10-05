import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { LegalFooter } from './LegalFooter'

describe('LegalFooter', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('links the configured privacy notice and imprint', async () => {
    fakeApi({ 'GET /api/v1/legal': () => json({ privacyNoticeUrl: '/rechtliches/datenschutz.html', imprintUrl: 'https://example.com/impressum' }) })
    render(<LegalFooter signedIn={false} />)

    expect(await screen.findByRole('link', { name: 'Datenschutz' })).toHaveAttribute('href', '/rechtliches/datenschutz.html')
    expect(screen.getByRole('link', { name: 'Impressum' })).toHaveAttribute('href', 'https://example.com/impressum')
    expect(screen.queryByRole('button', { name: 'Meine Daten herunterladen' })).not.toBeInTheDocument()
  })

  it('downloads the own data of a signed-in person', async () => {
    const api = fakeApi({
      'GET /api/v1/legal': () => json({ privacyNoticeUrl: null, imprintUrl: null }),
      'GET /api/v1/me/data-export': () => json({ profile: {} }),
    })
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }))
    render(<LegalFooter signedIn />)

    await userEvent.click(screen.getByRole('button', { name: 'Meine Daten herunterladen' }))

    expect(api.calls.some((call) => call.key === 'GET /api/v1/me/data-export')).toBe(true)
    expect(screen.queryByRole('link', { name: 'Datenschutz' })).not.toBeInTheDocument()
  })
})
