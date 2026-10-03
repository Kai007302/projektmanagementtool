import { useEffect, useState } from 'react'
import { fetchActivity, type Activity } from './api'

const actionText: Record<string, string> = {
  ProjectCreated: 'hat das Projekt angelegt',
  ProjectUpdated: 'hat das Projekt geändert',
  ProjectDeleted: 'hat das Projekt gelöscht',
  MemberAdded: 'hat ein Mitglied hinzugefügt',
  MemberRoleChanged: 'hat eine Rolle geändert',
  MemberRemoved: 'hat ein Mitglied entfernt',
  TaskCreated: 'hat eine Aufgabe angelegt',
  TaskUpdated: 'hat eine Aufgabe geändert',
  TaskDeleted: 'hat eine Aufgabe gelöscht',
  TaskMoved: 'hat eine Aufgabe verschoben',
  BoardColumnCreated: 'hat eine Spalte angelegt',
  BoardColumnUpdated: 'hat eine Spalte geändert',
  BoardColumnDeleted: 'hat eine Spalte gelöscht',
  DependencyAdded: 'hat eine Abhängigkeit angelegt',
  DependencyRemoved: 'hat eine Abhängigkeit entfernt',
  MilestoneCreated: 'hat einen Meilenstein angelegt',
  MilestoneUpdated: 'hat einen Meilenstein geändert',
  MilestoneDeleted: 'hat einen Meilenstein gelöscht',
  CommentAdded: 'hat kommentiert',
  AttachmentAdded: 'hat eine Datei angehängt',
  AttachmentDeleted: 'hat eine Datei entfernt',
}

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

function describe(entry: Activity): string {
  const name = entry.metadata.Title ?? entry.metadata.Name
  const title = typeof name === 'string' ? ` „${name}“` : ''
  const column = typeof entry.metadata.Column === 'string' ? ` nach „${entry.metadata.Column}“` : ''
  return `${actionText[entry.action] ?? entry.action}${title}${column}`
}

export function ActivityFeed({ projectId, revision }: { projectId: string; revision: number }) {
  const [entries, setEntries] = useState<Activity[]>([])

  useEffect(() => {
    fetchActivity(projectId).then((page) => setEntries(page.items), () => setEntries([]))
  }, [projectId, revision])

  return (
    <section className="panel" aria-labelledby="activity-heading">
      <h3 id="activity-heading">Aktivität</h3>
      <ol className="plain-list activity">
        {entries.map((entry) => (
          <li key={entry.id}>
            <strong>{entry.actorName ?? 'Unbekannt'}</strong> {describe(entry)}
            <br />
            <small className="muted">{dateFormat.format(new Date(entry.createdAt))}</small>
          </li>
        ))}
      </ol>
    </section>
  )
}
