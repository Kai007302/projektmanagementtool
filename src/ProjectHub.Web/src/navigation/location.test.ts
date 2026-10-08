import { describe, expect, it } from 'vitest'
import { knowledgePath, parsePlace, projectPath } from './location'

const project = '0192a000-0000-7000-8000-000000000001'

describe('location', () => {
  it('reads the project and its view from the address', () => {
    expect(parsePlace(`/projekte/${project}/liste`)).toMatchObject({ area: 'projects', projectId: project, view: 'list' })
    expect(parsePlace(`/projekte/${project}`)).toMatchObject({ area: 'projects', projectId: project, view: null })
  })

  it('reads the knowledge pages and the other areas', () => {
    expect(parsePlace(`/wissen/${project}`)).toMatchObject({ area: 'knowledge', articleId: project })
    expect(parsePlace('/wissen/galaxie')).toMatchObject({ area: 'knowledge', articleId: null, knowledgeMode: 'galaxy' })
    expect(parsePlace('/verwaltung').area).toBe('admin')
    expect(parsePlace('/assistent').area).toBe('assistant')
  })

  it('opens the start page for anything it does not know', () => {
    expect(parsePlace('/')).toMatchObject({ area: 'projects', projectId: null })
    expect(parsePlace('/projekte/kein-projekt')).toMatchObject({ area: 'projects', projectId: null })
    expect(parsePlace('/irgendwas')).toMatchObject({ area: 'projects', projectId: null })
  })

  it('writes addresses that read back to the same page', () => {
    expect(parsePlace(projectPath(project, 'gantt'))).toMatchObject({ projectId: project, view: 'gantt' })
    expect(projectPath(project, 'board')).toBe(`/projekte/${project}`)
    expect(parsePlace(knowledgePath(null, 'galaxy')).knowledgeMode).toBe('galaxy')
    expect(knowledgePath(project)).toBe(`/wissen/${project}`)
  })
})
