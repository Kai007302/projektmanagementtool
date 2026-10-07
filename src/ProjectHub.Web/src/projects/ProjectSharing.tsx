import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import { fetchDepartments } from '../departments/api'
import type { Me } from '../identity/api'
import { toast } from '../ui/toast'
import { projectVisibilities, updateProject, type ProjectDetails, type ProjectVisibility } from './api'

type Option = { id: string; name: string }

/**
 * Who reads along in a project and which department it belongs to (ADR 0021). For those who manage the project;
 * only organization admins and the department's leads share it with the whole organization.
 */
export function ProjectSharing({ project, me, onChanged }: { project: ProjectDetails; me: Me; onChanged: () => void }) {
  const current = project.visibility ?? 'department'
  const [visibility, setVisibility] = useState<ProjectVisibility>(current)
  const [departmentId, setDepartmentId] = useState(project.departmentId ?? '')
  const [departments, setDepartments] = useState<Option[]>(() => me.departments.filter((d) => d.role !== 'guest'))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Organization admins may move projects to any department.
  useEffect(() => {
    if (me.organizationRole !== 'admin') return
    fetchDepartments().then((page) => setDepartments(page.items), () => {})
  }, [me.organizationRole])

  const options = departments.some((d) => d.id === project.departmentId) || !project.departmentId
    ? departments
    : [{ id: project.departmentId, name: project.departmentName ?? 'Aktuelle Abteilung' }, ...departments]
  const canShare = project.capabilities.canShareWithOrganization === true

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    const changes: Parameters<typeof updateProject>[2] = {}
    if (visibility !== current) changes.visibility = visibility
    if (departmentId && departmentId !== project.departmentId) changes.departmentId = departmentId
    try {
      if (Object.keys(changes).length > 0) {
        await updateProject(project.id, project.version, changes)
        toast('Gespeichert.')
      }
      onChanged()
    } catch (e) {
      setError(
        e instanceof ApiError && e.status === 409
          ? 'Jemand anderes hat das Projekt inzwischen geändert. Bitte noch einmal versuchen.'
          : (e as Error).message,
      )
      onChanged()
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="stacked-form" onSubmit={(event) => void submit(event)}>
      <fieldset className="template-picker">
        <legend>Wer liest mit?</legend>
        {(Object.keys(projectVisibilities) as ProjectVisibility[]).map((value) => (
          <label key={value} className="template-option">
            <input
              type="radio"
              name="visibility"
              value={value}
              checked={visibility === value}
              disabled={value === 'organization' && !canShare && current !== 'organization'}
              onChange={() => setVisibility(value)}
            />
            <span>
              <strong>{projectVisibilities[value].label}</strong>
              <small className="muted">{projectVisibilities[value].hint}</small>
            </span>
          </label>
        ))}
      </fieldset>
      {!canShare && <p className="muted">Für die ganze Organisation freigeben können die Abteilungsleitung und Admins.</p>}
      <label>
        Abteilung
        <select value={departmentId} onChange={(event) => setDepartmentId(event.target.value)}>
          {options.map((department) => (
            <option key={department.id} value={department.id}>
              {department.name}
            </option>
          ))}
        </select>
      </label>
      <button type="submit" disabled={busy}>
        Speichern
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
