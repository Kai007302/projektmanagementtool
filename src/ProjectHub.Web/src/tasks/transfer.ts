import { apiDownload, apiFetch, isoToday, safeFileName } from '../api/client'

export type TaskFileFormat = 'csv' | 'xlsx'

export type TaskImportError = { row: number; column: string; code: string; message: string }

/** created is 0 for a preview and whenever there are errors: then nothing was imported. */
export type TaskImportResult = { rows: number; created: number; ignoredColumns: string[]; errors: TaskImportError[] }

/** What went wrong in a row, for people: the server's codes in German. */
export const importErrorText: Record<string, string> = {
  required: 'Titel fehlt',
  invalid: 'Ungültiger Wert',
  unknown_status: 'Status unbekannt (Offen, In Arbeit, Erledigt)',
  unknown_priority: 'Priorität unbekannt (Niedrig, Normal, Hoch, Dringend)',
  invalid_date: 'Kein Datum (z. B. 30.11.2026)',
  invalid_number: 'Keine gültige Zahl',
  unknown_assignee: 'Person ist nicht Mitglied mit Schreibrecht in diesem Projekt',
  missing_title_column: 'Die erste Zeile braucht eine Spalte „Titel“',
}

export const exportTasks = (projectId: string, projectName: string, format: TaskFileFormat) =>
  apiDownload(
    `/api/v1/projects/${projectId}/tasks/export?format=${format}`,
    `${safeFileName(projectName, 'projekt')} Aufgaben ${isoToday()}.${format}`,
  )

/** Checks the file (dryRun) or imports it: all rows or none. */
export function importTasks(projectId: string, file: File, dryRun: boolean) {
  const form = new FormData()
  form.append('file', file)
  return apiFetch<TaskImportResult>(`/api/v1/projects/${projectId}/tasks/import?dryRun=${dryRun}`, { method: 'POST', body: form })
}
