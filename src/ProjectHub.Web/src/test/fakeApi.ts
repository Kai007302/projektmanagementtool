import { vi } from 'vitest'

type Handler = (init: RequestInit | undefined, headers: Headers) => Response | Promise<Response>

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** Routes fetch calls by "METHOD path" to handlers; unknown routes answer 404. */
export function fakeApi(routes: Record<string, Handler>) {
  const calls: { key: string; init?: RequestInit; headers: Headers }[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${String(input)}`
    const headers = new Headers(init?.headers)
    calls.push({ key, init, headers })
    const handler = routes[key]
    return handler ? handler(init, headers) : json({ title: 'Not found' }, 404)
  })
  vi.stubGlobal('fetch', fetchMock)
  return { calls }
}
