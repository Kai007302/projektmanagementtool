import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fakeApi, json } from '../test/fakeApi'
import { parseEvents, type AiStatus } from './api'
import { AnswerText } from './AnswerText'
import { AssistantPage } from './AssistantPage'

const status: AiStatus = { enabled: true, provider: 'anthropic', model: 'claude-opus-5-5', mcp: true }
const kickoff = '01920000-0000-7000-8000-000000000701'

function sse(events: object[]) {
  return new Response(events.map((e) => `event: ${(e as { type: string }).type}\ndata: ${JSON.stringify(e)}\n\n`).join(''), {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  })
}

describe('parseEvents', () => {
  it('keeps an incomplete event for the next chunk', () => {
    const { events, rest } = parseEvents('event: delta\ndata: {"type":"delta","text":"Hal"}\n\nevent: delta\ndata: {"type":"del')

    expect(events).toEqual([{ type: 'delta', text: 'Hal' }])
    expect(rest).toBe('event: delta\ndata: {"type":"del')
  })
})

describe('AnswerText', () => {
  it('links articles but never external addresses or images', async () => {
    const onOpen = vi.fn()
    render(
      <AnswerText
        text={`Siehe [Kickoff](article:${kickoff}) und **wichtig**.\n\n- [Klick](https://evil.example/?d=geheim)\n- ![Bild](https://evil.example/x.png)`}
        onOpenArticle={onOpen}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Kickoff' }))
    expect(onOpen).toHaveBeenCalledWith(kickoff)
    expect(screen.getByText('wichtig').tagName).toBe('STRONG')
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('Klick')).toBeInTheDocument()
  })
})

describe('AssistantPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('streams the answer, shows what it looks up and lists the sources', async () => {
    const { calls } = fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [{ id: 'p-1', name: 'Intranet-Relaunch' }], nextOffset: null }),
      'POST /api/v1/ai/chat': () =>
        sse([
          { type: 'tool', tool: 'search_knowledge' },
          { type: 'delta', text: 'Laut ' },
          { type: 'delta', text: `[Projekt-Kickoff durchführen](article:${kickoff}) gilt:` },
          { type: 'sources', sources: [{ articleId: kickoff, title: 'Projekt-Kickoff durchführen', cited: true }] },
          { type: 'done' },
        ]),
    })
    const onOpen = vi.fn()
    render(<AssistantPage status={status} onOpenArticle={onOpen} />)

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Bezug' }), 'p-1')
    await userEvent.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Wie läuft ein Kickoff ab?{Enter}')

    const answer = await screen.findByRole('article', { name: 'Antwort des Assistenten' })
    expect(await within(answer).findByText(/gilt:/)).toBeInTheDocument()
    await userEvent.click(within(answer).getAllByRole('button', { name: 'Projekt-Kickoff durchführen' })[1])
    expect(onOpen).toHaveBeenCalledWith(kickoff)
    const body = JSON.parse(String(calls.find((c) => c.key === 'POST /api/v1/ai/chat')!.init!.body))
    expect(body).toEqual({ messages: [{ role: 'user', text: 'Wie läuft ein Kickoff ab?' }], projectId: 'p-1' })
  })

  it('shows a failure of the model as an alert and sends earlier turns along', async () => {
    let call = 0
    const { calls } = fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [], nextOffset: null }),
      'POST /api/v1/ai/chat': () =>
        ++call === 1
          ? sse([{ type: 'delta', text: 'Erste Antwort' }, { type: 'done' }])
          : sse([{ type: 'error', text: 'Der KI-Dienst ist gerade nicht erreichbar.' }]),
    })
    render(<AssistantPage status={status} onOpenArticle={() => {}} />)

    await userEvent.click(screen.getByRole('button', { name: 'Wie läuft ein Projekt-Kickoff ab?' }))
    await screen.findByText('Erste Antwort')
    await userEvent.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Und weiter?{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent('nicht erreichbar')
    const second = JSON.parse(String(calls.filter((c) => c.key === 'POST /api/v1/ai/chat')[1].init!.body))
    expect(second.messages).toEqual([
      { role: 'user', text: 'Wie läuft ein Projekt-Kickoff ab?' },
      { role: 'assistant', text: 'Erste Antwort' },
      { role: 'user', text: 'Und weiter?' },
    ])
  })

  it('runs a proposed change only after the person approved it', async () => {
    let call = 0
    const { calls } = fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [], nextOffset: null }),
      'POST /api/v1/ai/chat': () =>
        ++call === 1
          ? sse([
              { type: 'delta', text: 'Ich lege die Aufgabe an.' },
              {
                type: 'approval',
                continuation: 'geheim',
                actions: [
                  {
                    id: 'a-1',
                    tool: 'create_task',
                    title: 'Aufgabe anlegen',
                    destructive: false,
                    details: [
                      { label: 'Projekt', value: 'Intranet-Relaunch' },
                      { label: 'Titel', value: 'Protokoll schreiben' },
                    ],
                  },
                ],
              },
              { type: 'done' },
            ])
          : sse([{ type: 'tool', tool: 'create_task' }, { type: 'delta', text: 'Erledigt.' }, { type: 'done' }]),
    })
    render(<AssistantPage status={{ ...status, actions: true }} onOpenArticle={() => {}} />)

    await userEvent.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Leg eine Aufgabe an{Enter}')
    const card = await screen.findByRole('region', { name: 'Vorschlag: Aufgabe anlegen' })
    expect(within(card).getByText('Protokoll schreiben')).toBeInTheDocument()
    expect(calls.filter((c) => c.key === 'POST /api/v1/ai/chat')).toHaveLength(1)

    await userEvent.click(within(card).getByRole('button', { name: 'Ausführen' }))

    expect(await screen.findByText(/Erledigt\./)).toBeInTheDocument()
    expect(within(card).getByText('Freigegeben')).toBeInTheDocument()
    const second = JSON.parse(String(calls.filter((c) => c.key === 'POST /api/v1/ai/chat')[1].init!.body))
    expect(second).toEqual({
      messages: [{ role: 'user', text: 'Leg eine Aufgabe an' }],
      projectId: null,
      continuation: 'geheim',
      approvals: [{ id: 'a-1', approved: true }],
    })
  })

  it('drops a waiting change when the person asks something else', async () => {
    let call = 0
    const { calls } = fakeApi({
      'GET /api/v1/projects?limit=100': () => json({ items: [], nextOffset: null }),
      'POST /api/v1/ai/chat': () =>
        ++call === 1
          ? sse([
              {
                type: 'approval',
                continuation: 'geheim',
                actions: [{ id: 'a-1', tool: 'delete_task', title: 'Aufgabe löschen', destructive: true, details: [] }],
              },
              { type: 'done' },
            ])
          : sse([{ type: 'delta', text: 'Okay.' }, { type: 'done' }]),
    })
    render(<AssistantPage status={{ ...status, actions: true }} onOpenArticle={() => {}} />)

    await userEvent.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Lösch die Aufgabe{Enter}')
    const card = await screen.findByRole('region', { name: 'Vorschlag: Aufgabe löschen' })
    expect(within(card).getByRole('button', { name: 'Löschen' })).toBeInTheDocument()
    await userEvent.type(screen.getByRole('textbox', { name: 'Deine Frage' }), 'Doch nicht{Enter}')

    expect(await within(card).findByText('Nicht ausgeführt')).toBeInTheDocument()
    const second = JSON.parse(String(calls.filter((c) => c.key === 'POST /api/v1/ai/chat')[1].init!.body))
    expect(second.continuation).toBeUndefined()
  })
})
