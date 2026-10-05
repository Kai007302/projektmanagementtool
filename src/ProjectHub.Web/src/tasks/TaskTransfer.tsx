import { useId, useState } from 'react'
import type { ProjectDetails } from '../projects/api'
import { exportTasks, importErrorText, importTasks, type TaskFileFormat, type TaskImportResult } from './transfer'

type Props = { project: ProjectDetails; onImported: () => void }

/** Tasks as CSV or Excel file: download all of them, or import new ones after a check of the file. */
export function TaskTransfer({ project, onImported }: Props) {
  const [error, setError] = useState<string | null>(null)
  const [file, setFile] = useState<File | null>(null)
  const [check, setCheck] = useState<TaskImportResult | null>(null)
  const [done, setDone] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const inputId = useId()

  async function download(format: TaskFileFormat) {
    setError(null)
    try {
      await exportTasks(project.id, project.name, format)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  async function choose(selected: File | null) {
    setFile(selected)
    setCheck(null)
    setDone(null)
    setError(null)
    if (!selected) return
    setBusy(true)
    try {
      setCheck(await importTasks(project.id, selected, true))
    } catch (e) {
      setError(`Die Datei konnte nicht gelesen werden: ${(e as Error).message}`)
    } finally {
      setBusy(false)
    }
  }

  async function runImport() {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const result = await importTasks(project.id, file, false)
      if (result.errors.length > 0) {
        setCheck(result)
      } else {
        setDone(result.created)
        setFile(null)
        setCheck(null)
        onImported()
      }
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const importable = check !== null && check.errors.length === 0 && check.rows > 0

  return (
    <div className="task-transfer">
      <div className="row" role="group" aria-label="Aufgaben exportieren">
        <span className="muted">Exportieren:</span>
        <button type="button" className="link-button" onClick={() => void download('xlsx')}>
          Excel
        </button>
        <button type="button" className="link-button" onClick={() => void download('csv')}>
          CSV
        </button>
      </div>
      {project.capabilities.canContribute && (
        <div className="task-import">
          <label htmlFor={inputId}>Aufgaben importieren (Excel oder CSV)</label>
          <input
            id={inputId}
            type="file"
            accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            disabled={busy}
            onChange={(event) => void choose(event.target.files?.[0] ?? null)}
          />
          <p className="muted">
            Erste Zeile mit Spaltennamen, mindestens „Titel“. Weitere Spalten wie im Export: Beschreibung, Status, Priorität,
            Zuständig (E-Mail), Start, Fällig, Fortschritt (%), Aufwand (h). Es werden immer neue Aufgaben angelegt.
          </p>
          {busy && <p role="status">Datei wird geprüft …</p>}
          {check && (
            <div className="task-import-check" role="status">
              {check.errors.length === 0 ? (
                <p>
                  {check.rows === 1 ? '1 Aufgabe erkannt.' : `${check.rows} Aufgaben erkannt.`}
                  {check.ignoredColumns.length > 0 && ` Nicht übernommen: ${check.ignoredColumns.join(', ')}.`}
                </p>
              ) : (
                <>
                  <p>
                    <strong>
                      {check.errors.length === 1 ? '1 Problem' : `${check.errors.length} Probleme`} in der Datei. Es wird nichts
                      importiert, bis alle behoben sind.
                    </strong>
                  </p>
                  <ul className="task-import-errors">
                    {check.errors.slice(0, 20).map((e, index) => (
                      <li key={index}>
                        Zeile {e.row}, {e.column}: {importErrorText[e.code] ?? e.message}
                      </li>
                    ))}
                  </ul>
                  {check.errors.length > 20 && <p className="muted">… und {check.errors.length - 20} weitere.</p>}
                </>
              )}
            </div>
          )}
          {importable && (
            <button type="button" disabled={busy} onClick={() => void runImport()}>
              {check.rows === 1 ? '1 Aufgabe importieren' : `${check.rows} Aufgaben importieren`}
            </button>
          )}
          {done !== null && <p role="status">{done === 1 ? '1 Aufgabe importiert. 🎉' : `${done} Aufgaben importiert. 🎉`}</p>}
        </div>
      )}
      {error && <p role="alert">{error}</p>}
    </div>
  )
}
