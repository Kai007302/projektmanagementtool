/**
 * The page shown is kept in the address, so reloading the browser (or signing in again) opens the same page instead
 * of the start page. Each page writes its own part of the address when it changes; the address only replaces the
 * current history entry, so the browser's back button behaves as before.
 */

export type Area = 'projects' | 'knowledge' | 'admin' | 'assistant'

export type ProjectView = 'board' | 'list' | 'gantt' | 'whiteboard' | 'knowledge' | 'activity' | 'webex'

export type KnowledgeMode = 'articles' | 'galaxy'

export type Place = {
  area: Area
  projectId: string | null
  view: ProjectView | null
  articleId: string | null
  knowledgeMode: KnowledgeMode | null
}

const areaPaths: Record<Area, string> = { projects: '', knowledge: 'wissen', admin: 'verwaltung', assistant: 'assistent' }

const viewPaths: Record<ProjectView, string> = {
  board: 'board',
  list: 'liste',
  gantt: 'gantt',
  whiteboard: 'whiteboard',
  knowledge: 'wissen',
  activity: 'aktivitaet',
  webex: 'webex',
}

const id = /^[0-9a-f-]{36}$/i

const findKey = <T extends string>(map: Record<T, string>, value: string | undefined) =>
  value === undefined ? null : ((Object.keys(map) as T[]).find((key) => map[key] === value) ?? null)

/** Reads a path such as /projekte/<id>/liste; anything unknown is the start page. */
export function parsePlace(path: string): Place {
  const [first, second, third] = path.split('/').filter(Boolean).map(decodeURIComponent)
  const place: Place = { area: 'projects', projectId: null, view: null, articleId: null, knowledgeMode: null }
  if (first === 'projekte' && second && id.test(second)) {
    return { ...place, projectId: second, view: findKey(viewPaths, third) }
  }

  if (first === 'wissen') {
    if (second === 'galaxie') return { ...place, area: 'knowledge', knowledgeMode: 'galaxy' }
    return { ...place, area: 'knowledge', articleId: second && id.test(second) ? second : null }
  }

  const area = findKey(areaPaths, first)
  return area && area !== 'projects' ? { ...place, area } : place
}

export const areaPath = (area: Area) => `/${areaPaths[area]}`

export const projectPath = (projectId: string, view: ProjectView) =>
  `/projekte/${projectId}${view === 'board' ? '' : `/${viewPaths[view]}`}`

export const knowledgePath = (articleId: string | null, mode: KnowledgeMode = 'articles') =>
  articleId ? `/wissen/${articleId}` : mode === 'galaxy' ? '/wissen/galaxie' : '/wissen'

/** The page in the address right now; read on start to open the page the browser was on. */
export const currentPlace = () => parsePlace(window.location.pathname)

/** Puts the path in the address without a new history entry. */
export function showPath(path: string) {
  if (window.location.pathname !== path) window.history.replaceState(window.history.state, '', path + window.location.search + window.location.hash)
}
