import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { AnonymizePersonForm } from './AnonymizePersonForm'

const hanna = { id: 'u-hanna', displayName: 'Hanna Test', email: 'hanna@x', department: null, status: 'active' }
const ada = { id: 'u-ada', displayName: 'Ada Admin', email: 'ada@x', department: null, status: 'active' }

describe('AnonymizePersonForm', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('anonymizes a found person after confirmation, never the admin themselves', async () => {
    const api = fakeApi({
      'GET /api/v1/users?search=a&limit=10': () => json({ items: [ada, hanna], nextOffset: null }),
      'POST /api/v1/admin/users/u-hanna/anonymize': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<AnonymizePersonForm myId="u-ada" />)

    await userEvent.click(screen.getByText(/Person anonymisieren/))
    await userEvent.type(screen.getByLabelText('Name oder E-Mail'), 'a')
    await userEvent.click(screen.getByRole('button', { name: 'Suchen' }))
    expect(await screen.findByText(/Hanna Test/)).toBeInTheDocument()
    expect(screen.queryByText(/Ada Admin/)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Anonymisieren' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Hanna Test wurde anonymisiert.')
    expect(api.calls.some((call) => call.key === 'POST /api/v1/admin/users/u-hanna/anonymize')).toBe(true)
  })

  it('does nothing without confirmation', async () => {
    const api = fakeApi({ 'GET /api/v1/users?search=h&limit=10': () => json({ items: [hanna], nextOffset: null }) })
    vi.stubGlobal('confirm', vi.fn(() => false))
    render(<AnonymizePersonForm myId="u-ada" />)

    await userEvent.click(screen.getByText(/Person anonymisieren/))
    await userEvent.type(screen.getByLabelText('Name oder E-Mail'), 'h')
    await userEvent.click(screen.getByRole('button', { name: 'Suchen' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Anonymisieren' }))

    expect(api.calls.some((call) => call.key.startsWith('POST'))).toBe(false)
  })
})
