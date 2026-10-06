import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { LegalFooter } from './LegalFooter'

describe('LegalFooter', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('links the configured privacy notice and imprint, nothing else', async () => {
    fakeApi({ 'GET /api/v1/legal': () => json({ privacyNoticeUrl: '/rechtliches/datenschutz.html', imprintUrl: 'https://example.com/impressum' }) })
    render(<LegalFooter />)

    expect(await screen.findByRole('link', { name: 'Datenschutz' })).toHaveAttribute('href', '/rechtliches/datenschutz.html')
    expect(screen.getByRole('link', { name: 'Impressum' })).toHaveAttribute('href', 'https://example.com/impressum')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
