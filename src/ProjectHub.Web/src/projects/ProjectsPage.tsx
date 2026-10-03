import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { createProject, fetchProjects, projectRoles, projectStatuses, type ProjectSummary } from './api'
import { ProjectView } from './ProjectView'

export function ProjectsPage({ me }: { me: Me }) {
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchProjects().then(
      (page) => setProjects(page.items),
      (e: Error) => setError(e.message),
    )
  }, [])

  useEffect(load, [load])

  if (selected) {
    return (
      <ProjectView
        key={selected}
        projectId={selected}
        me={me}
        onBack={() => {
          setSelected(null)
          load()
        }}
      />
    )
  }

  return (
    <section aria-labelledby="projects-heading">
      <h2 id="projects-heading">Projekte</h2>
      {error && <p role="alert">{error}</p>}
      <CreateProjectForm
        onCreated={(project) => {
          load()
          setSelected(project.id)
        }}
      />
      {projects === null ? (
        <p>Projekte werden geladen …</p>
      ) : projects.length === 0 ? (
        <p>Du bist noch in keinem Projekt.</p>
      ) : (
        <ul className="card-list">
          {projects.map((project) => (
            <li key={project.id}>
              <button type="button" className="card" onClick={() => setSelected(project.id)}>
                <span className="card-title">{project.name}</span>
                <span className="card-meta">
                  {projectStatuses[project.status]}
                  {project.myRole && ` · ${projectRoles[project.myRole]}`}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function CreateProjectForm({ onCreated }: { onCreated: (project: ProjectSummary) => void }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const project = await createProject(name, '')
      setName('')
      onCreated(project)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <label>
        Neues Projekt
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
      </label>
      <button type="submit">Projekt anlegen</button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
