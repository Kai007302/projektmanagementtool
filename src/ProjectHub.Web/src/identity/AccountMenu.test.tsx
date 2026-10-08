import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import type { Me } from './api'
import { AccountMenu } from './AccountMenu'

const ben: Me = { id: 'u-ben', displayName: 'Ben Projektleiter', email: 'ben@x', organizationId: 'org-1', organizationRole: 'member', departments: [] }

describe('AccountMenu', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('offers the own calendar and the own data export behind the avatar', async () => {
    const api = fakeApi({
      'GET /api/v1/me/data-export': () => json({ profile: {} }),
      'GET /api/v1/me/calendar-feed': () => json({ active: false, createdAt: null, lastUsedAt: null }),
    })
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }))
    render(<AccountMenu me={ben} role="Mitglied" />)

    await userEvent.click(screen.getByRole('button', { name: 'Konto von Ben Projektleiter' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Meine Daten herunterladen' }))
    expect(api.calls.some((call) => call.key === 'GET /api/v1/me/data-export')).toBe(true)

    await userEvent.click(screen.getByRole('button', { name: 'Konto von Ben Projektleiter' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Meine Termine abonnieren' }))
    const dialog = screen.getByRole('dialog', { name: 'Meine Termine abonnieren' })
    expect(await screen.findByRole('button', { name: 'Kalender-Adresse erstellen' })).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    expect(dialog).not.toBeInTheDocument()
  })
})
