import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

function mockFetch(response: Promise<Response>) {
  vi.stubGlobal('fetch', vi.fn(() => response))
}

describe('App', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the product name', () => {
    mockFetch(new Promise(() => {}))
    render(<App />)
    expect(screen.getByRole('heading', { name: 'ProjectHub' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Verbindung wird geprüft')
  })

  it('reports a ready backend', async () => {
    mockFetch(Promise.resolve(new Response('Healthy', { status: 200 })))
    render(<App />)
    expect(await screen.findByText('Backend bereit')).toBeInTheDocument()
  })

  it('reports an unavailable backend', async () => {
    mockFetch(Promise.resolve(new Response('Unhealthy', { status: 503 })))
    render(<App />)
    expect(await screen.findByText('Backend nicht erreichbar')).toBeInTheDocument()
  })

  it('reports an unavailable backend when the request fails', async () => {
    mockFetch(Promise.reject(new TypeError('Failed to fetch')))
    render(<App />)
    expect(await screen.findByText('Backend nicht erreichbar')).toBeInTheDocument()
  })
})
