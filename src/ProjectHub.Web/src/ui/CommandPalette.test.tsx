import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { CommandPalette } from './CommandPalette'

const project = (id: string, name: string) => ({ id, name, description: null, status: 'active', myRole: 'admin', version: 1 })

describe('CommandPalette', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('finds areas, projects and articles and opens the chosen one with the keyboard', async () => {
    fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [project('p-1', 'Website-Relaunch'), project('p-2', 'Intranet')], nextOffset: null }),
      'GET /api/v1/knowledge/articles?limit=100&q=Web': () =>
        json({ items: [{ id: 'a-1', title: 'Webex einrichten', summary: 'So geht es', articleType: 'how_to', status: 'published', visibility: 'organization', spaceName: null, tags: [] }], nextOffset: null }),
    })
    const onOpenProject = vi.fn()
    const onOpenArticle = vi.fn()
    const onClose = vi.fn()
    const goToKnowledge = vi.fn()
    render(<CommandPalette destinations={[{ label: 'Wissen', go: goToKnowledge }]} onOpenProject={onOpenProject} onOpenArticle={onOpenArticle} onClose={onClose} />)

    const input = screen.getByRole('combobox', { name: 'Suchen oder springen' })
    expect(input).toHaveFocus()
    expect(await screen.findByRole('option', { name: /Website-Relaunch/ })).toBeInTheDocument()

    await userEvent.type(input, 'Web')
    expect(await screen.findByRole('option', { name: /Webex einrichten/ })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Intranet/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Website-Relaunch/ })).toHaveAttribute('aria-selected', 'true')

    await userEvent.keyboard('{ArrowDown}{Enter}')
    expect(onClose).toHaveBeenCalled()
    expect(onOpenArticle).toHaveBeenCalledWith('a-1')
    expect(onOpenProject).not.toHaveBeenCalled()
  })

  it('offers to create a project or an article when nothing is found', async () => {
    fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [project('p-1', 'Website-Relaunch')], nextOffset: null }),
      'GET /api/v1/knowledge/articles?limit=100&q=Messe': () => json({ items: [], nextOffset: null }),
    })
    const onCreateProject = vi.fn()
    const onCreateArticle = vi.fn()
    const onClose = vi.fn()
    render(
      <CommandPalette
        destinations={[]}
        onOpenProject={() => {}}
        onOpenArticle={() => {}}
        onCreateProject={onCreateProject}
        onCreateArticle={onCreateArticle}
        onClose={onClose}
      />,
    )

    await userEvent.type(screen.getByRole('combobox', { name: 'Suchen oder springen' }), 'Web')
    expect(screen.queryByRole('option', { name: /anlegen/ })).not.toBeInTheDocument()

    await userEvent.clear(screen.getByRole('combobox', { name: 'Suchen oder springen' }))
    await userEvent.type(screen.getByRole('combobox', { name: 'Suchen oder springen' }), 'Messe')
    expect(screen.getByRole('option', { name: /Projekt „Messe“ anlegen/ })).toHaveAttribute('aria-selected', 'true')
    await userEvent.click(screen.getByRole('option', { name: /Artikel „Messe“ anlegen/ }))

    expect(onCreateArticle).toHaveBeenCalledWith('Messe')
    expect(onCreateProject).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('closes on Escape', async () => {
    fakeApi({ 'GET /api/v1/projects?limit=100': () => json({ items: [], nextOffset: null }) })
    const onClose = vi.fn()
    render(<CommandPalette destinations={[]} onOpenProject={() => {}} onOpenArticle={() => {}} onClose={onClose} />)

    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })
})
