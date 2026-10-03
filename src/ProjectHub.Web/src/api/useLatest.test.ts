import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useLatest } from './useLatest'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('useLatest', () => {
  it('applies only the newest answer, even when an older one arrives last', async () => {
    const { result } = renderHook(() => useLatest())
    const applied: string[] = []
    const first = deferred<string>()
    const second = deferred<string>()

    result.current(first.promise).then((v) => applied.push(v))
    result.current(second.promise).then((v) => applied.push(v))
    await act(async () => {
      second.resolve('new')
      first.resolve('old')
      await Promise.resolve()
    })

    expect(applied).toEqual(['new'])
  })

  it('drops errors of outdated requests', async () => {
    const { result } = renderHook(() => useLatest())
    const errors: string[] = []
    const first = deferred<string>()
    const second = deferred<string>()

    result.current(first.promise).catch((e: Error) => errors.push(e.message))
    result.current(second.promise).catch((e: Error) => errors.push(e.message))
    await act(async () => {
      first.reject(new Error('old'))
      second.reject(new Error('new'))
      await Promise.resolve()
    })

    expect(errors).toEqual(['new'])
  })

  it('drops loads that were on their way when something changed locally', async () => {
    const { result } = renderHook(() => useLatest())
    const applied: string[] = []
    const load = deferred<string>()

    result.current(load.promise).then((v) => applied.push(v))
    result.current.invalidate()
    await act(async () => {
      load.resolve('before the change')
      await Promise.resolve()
    })

    expect(applied).toEqual([])
  })
})
