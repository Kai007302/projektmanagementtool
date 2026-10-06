import { useState } from 'react'
import { CalendarFeedPanel } from '../calendar/CalendarFeedPanel'
import { exportTasks } from '../tasks/transfer'
import { Dialog } from '../ui/Dialog'
import { Menu, type MenuItem } from '../ui/Menu'
import type { ProjectSummary } from './api'
import { ProjectAppearance } from './ProjectAppearance'

type Props = {
  project: Pick<ProjectSummary, 'id' | 'name' | 'version' | 'icon' | 'logoVersion'>
  canEdit: boolean
  /** Further actions after the common ones, e.g. deleting the project. */
  extraItems?: MenuItem[]
  onChanged: () => void
  className?: string
}

/**
 * The "…" menu of a project, on its tile and in its header: subscribe to its calendar, download its tasks, and for
 * editors its symbol and logo. These belong to one project, so they are not on the overview page itself.
 */
export function ProjectMenu({ project, canEdit, extraItems = [], onChanged, className }: Props) {
  const [open, setOpen] = useState<'calendar' | 'appearance' | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function download() {
    setError(null)
    try {
      await exportTasks(project.id, project.name, 'xlsx')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <>
      <Menu
        className={className}
        label="Weitere Aktionen zum Projekt"
        items={[
          { label: 'Kalender abonnieren', onSelect: () => setOpen('calendar') },
          { label: 'Daten herunterladen (Excel)', onSelect: () => void download() },
          ...(canEdit ? [{ label: 'Symbol und Logo ändern', onSelect: () => setOpen('appearance') }] : []),
          ...extraItems,
        ]}
      />
      {error && (
        <p role="alert" className="project-menu-error">
          {error}
        </p>
      )}
      {open === 'calendar' && (
        <Dialog label={`Kalender von „${project.name}“ abonnieren`} onClose={() => setOpen(null)}>
          <CalendarFeedPanel project={project} />
        </Dialog>
      )}
      {open === 'appearance' && (
        <Dialog label={`Symbol und Logo von „${project.name}“`} onClose={() => setOpen(null)}>
          <ProjectAppearance project={project} onChanged={onChanged} />
        </Dialog>
      )}
    </>
  )
}
