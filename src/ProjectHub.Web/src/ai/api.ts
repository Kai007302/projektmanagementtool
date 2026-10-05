import { apiFetch, apiStream, jsonBody } from '../api/client'

export type AiStatus = { enabled: boolean; provider: string; model: string | null; mcp: boolean }

export type ChatMessage = { role: 'user' | 'assistant'; text: string }

export type AssistantSource = { articleId: string; title: string; cited: boolean }

export type AssistantEvent =
  | { type: 'delta'; text: string }
  | { type: 'tool'; tool: string }
  | { type: 'sources'; sources: AssistantSource[] }
  | { type: 'done' }
  | { type: 'error'; text: string }

export const fetchAiStatus = () => apiFetch<AiStatus>('/api/v1/ai/status')

/** What the assistant is doing while it looks something up. */
export const toolText: Record<string, string> = {
  search_knowledge: 'Durchsucht das Wissen …',
  read_knowledge_article: 'Liest einen Wissensartikel …',
  list_projects: 'Sieht deine Projekte an …',
  get_project: 'Sieht das Projekt an …',
  list_tasks: 'Sieht die Aufgaben an …',
  get_task: 'Liest eine Aufgabe …',
}

/** Splits a server-sent event stream into events; an incomplete last block stays in the returned rest. */
export function parseEvents(buffer: string): { events: AssistantEvent[]; rest: string } {
  const blocks = buffer.replace(/\r\n/g, '\n').split('\n\n')
  const rest = blocks.pop() ?? ''
  const events = blocks
    .map((block) =>
      block
        .split('\n')
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).trimStart())
        .join('\n'),
    )
    .filter((data) => data.length > 0)
    .map((data) => JSON.parse(data) as AssistantEvent)
  return { events, rest }
}

/**
 * Sends the conversation and calls onEvent for every streamed event. The conversation lives only here in the
 * browser; the API stores none of it.
 */
export async function streamChat(
  messages: ChatMessage[],
  projectId: string | null,
  onEvent: (event: AssistantEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await apiStream('/api/v1/ai/chat', {
    method: 'POST',
    body: jsonBody({ messages, projectId }),
    headers: { Accept: 'text/event-stream' },
    signal,
  })
  if (!response.body) throw new Error('Keine Antwort erhalten.')

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    const parsed = parseEvents(buffer + value)
    buffer = parsed.rest
    parsed.events.forEach(onEvent)
  }
  parseEvents(buffer + '\n\n').events.forEach(onEvent)
}
