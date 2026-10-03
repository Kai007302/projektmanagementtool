export type ApiStatus = 'checking' | 'ready' | 'unavailable'

export async function fetchApiStatus(signal?: AbortSignal): Promise<ApiStatus> {
  try {
    const response = await fetch('/health/ready', { signal })
    return response.ok ? 'ready' : 'unavailable'
  } catch (error) {
    if (signal?.aborted) throw error
    return 'unavailable'
  }
}
