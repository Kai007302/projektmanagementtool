import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { DocChat } from './DocChat'

const done = () => new Response('event: delta\ndata: {"type":"delta","text":"Ja."}\n\nevent: done\ndata: {"type":"done"}\n\n', { headers: { 'Content-Type': 'text/event-stream' } })

describe('DocChat', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('asks about the open article first and about its space or everything on request', async () => {
    const user = userEvent.setup()
    const { calls } = fakeApi({
      'GET /api/v1/knowledge/spaces': () => json([{ id: 's-1', name: 'Labor', description: null, articleCount: 3, version: 1, useCase: 'experiment' }]),
      'POST /api/v1/ai/chat': done,
    })
    render(<DocChat article={{ id: 'a-1', title: 'Wärme', spaceId: 's-1' }} onOpenArticle={vi.fn()} onClose={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Welche Ergebnisse widersprechen sich?' })).toBeInTheDocument()
    await user.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Gilt das?{Enter}')
    expect(await screen.findByText('Ja.')).toBeInTheDocument()

    await user.selectOptions(screen.getByRole('combobox', { name: /Fragt in/ }), 'der ganzen Galaxie')
    await user.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Und sonst?{Enter}')
    await screen.findAllByText('Ja.')

    const bodies = calls.filter((c) => c.key === 'POST /api/v1/ai/chat').map((c) => JSON.parse(String(c.init!.body)))
    expect(bodies[0]).toMatchObject({ spaceId: 's-1', articleId: 'a-1' })
    expect(bodies[1]).toMatchObject({ spaceId: null, articleId: null })
  })
})
