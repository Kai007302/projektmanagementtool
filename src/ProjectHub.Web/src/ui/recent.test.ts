import { describe, expect, it } from 'vitest'
import { recentVisits, rememberVisit } from './recent'

describe('recent visits', () => {
  it('keeps the newest visit first, once, per person', () => {
    rememberVisit('u-1', { kind: 'project', id: 'p-1', title: 'Intranet' })
    rememberVisit('u-1', { kind: 'article', id: 'a-1', title: 'Onboarding' })
    rememberVisit('u-1', { kind: 'project', id: 'p-1', title: 'Intranet neu' })

    expect(recentVisits('u-1')).toEqual([
      { kind: 'project', id: 'p-1', title: 'Intranet neu' },
      { kind: 'article', id: 'a-1', title: 'Onboarding' },
    ])
    expect(recentVisits('u-2')).toEqual([])
  })
})
