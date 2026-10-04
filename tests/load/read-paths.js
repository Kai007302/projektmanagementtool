// Load test for the most frequent read paths (docs/OPERATIONS.md, Lasttest). Runs against an API in
// Development with synthetic users and switched-off rate limits, never against production data:
//
//   PROJECTHUB_RATE_LIMIT_PER_MINUTE=0 dotnet run --project src/ProjectHub.Api --launch-profile http
//   docker run --rm --network host -v "$PWD/tests/load:/load" grafana/k6 run /load/read-paths.js
//
// setup() creates one large project (TASKS tasks, a quarter of them with dates and a parent) and ARTICLES
// knowledge articles, then VUS virtual users read boards, Gantt charts, task lists and the knowledge hub.
import http from 'k6/http'
import { check, fail } from 'k6'

const api = (__ENV.API_URL || 'http://localhost:5080') + '/api/v1'
const tasks = Number(__ENV.TASKS || 1000)
const articles = Number(__ENV.ARTICLES || 300)

export const options = {
  setupTimeout: '5m',
  scenarios: {
    read: { executor: 'constant-vus', vus: Number(__ENV.VUS || 50), duration: __ENV.DURATION || '1m' },
  },
  // Targets for one API instance (DEC-032): 95 % of reads under 300 ms, under 1 % errors.
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:board}': ['p(95)<300'],
    'http_req_duration{name:gantt}': ['p(95)<300'],
    'http_req_duration{name:tasks}': ['p(95)<300'],
    'http_req_duration{name:projects}': ['p(95)<300'],
    'http_req_duration{name:activity}': ['p(95)<300'],
    'http_req_duration{name:unread}': ['p(95)<300'],
    'http_req_duration{name:articles}': ['p(95)<300'],
    'http_req_duration{name:search}': ['p(95)<300'],
    'http_req_duration{name:graph}': ['p(95)<300'],
  },
}

const as = (user) => ({ headers: { 'X-Dev-User': user, 'Content-Type': 'application/json' } })

function post(user, path, body) {
  const response = http.post(`${api}${path}`, JSON.stringify(body), as(user))
  if (response.status >= 300) fail(`${path}: ${response.status} ${response.body}`)
  return response.status === 204 ? null : response.json()
}

const day = (offset) => new Date(Date.UTC(2026, 9, 1 + offset)).toISOString().slice(0, 10)

export function setup() {
  const project = post('dev-ben', '/projects', { name: `Last ${Date.now()}` })
  for (const [user, role] of [['clara', 'editor'], ['david', 'member'], ['eva', 'viewer']]) {
    const me = http.get(`${api}/me`, as(`dev-${user}`)).json()
    post('dev-ben', `/projects/${project.id}/members`, { userId: me.id, role })
  }

  const parents = []
  for (let i = 0; i < tasks; i++) {
    const planned = i % 4 === 0
    const task = post('dev-ben', `/projects/${project.id}/tasks`, {
      title: `Aufgabe ${i}`,
      status: ['todo', 'in_progress', 'done'][i % 3],
      parentTaskId: planned && parents.length > 0 ? parents[i % parents.length] : null,
      startDate: planned ? day(i % 60) : null,
      dueDate: planned ? day((i % 60) + 5) : null,
    })
    if (i % 20 === 0) parents.push(task.id)
  }

  for (let i = 0; i < articles; i++) {
    post('dev-ada', '/knowledge/articles', {
      title: `Leitfaden ${i} Projektplanung`,
      articleType: 'how_to',
      summary: `Zusammenfassung ${i} zu Planung, Kanban und Gantt`,
      content: { blocks: [{ type: 'paragraph', text: `Inhalt ${i}: Aufgaben planen und Abhängigkeiten pflegen.` }] },
    })
  }

  return { projectId: project.id }
}

const projectReaders = ['dev-ben', 'dev-clara', 'dev-david', 'dev-eva']

export default function ({ projectId }) {
  const user = projectReaders[__VU % projectReaders.length]
  const get = (path, name, reader = user) => {
    const response = http.get(`${api}${path}`, { ...as(reader), tags: { name } })
    check(response, { [`${name} 200`]: (r) => r.status === 200 })
  }

  get('/projects?limit=100', 'projects')
  get(`/projects/${projectId}/board`, 'board')
  get(`/projects/${projectId}/gantt`, 'gantt')
  get(`/projects/${projectId}/tasks?limit=100`, 'tasks')
  get(`/projects/${projectId}/activity?limit=30`, 'activity')
  get('/me/notifications/unread-count', 'unread')
  get('/knowledge/articles?limit=50', 'articles', 'dev-ada')
  get('/knowledge/articles?q=Abh%C3%A4ngigkeiten&limit=20', 'search', 'dev-ada')
  get('/knowledge/graph', 'graph', 'dev-ada')
}
