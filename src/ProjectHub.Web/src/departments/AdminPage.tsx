import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { fetchUsers, type DepartmentRole, type Me, type User } from '../identity/api'
import { AnonymizePersonForm } from '../privacy/AnonymizePersonForm'
import { EmptyState } from '../ui/EmptyState'
import { CloseIcon } from '../ui/icons'
import { InlineEdit } from '../ui/InlineEdit'
import { Reveal } from '../ui/Reveal'
import { Skeleton } from '../ui/Skeleton'
import { toast } from '../ui/toast'
import {
  addDepartmentMember,
  changeDepartmentMember,
  createDepartment,
  deleteDepartment,
  departmentRoles,
  fetchDepartment,
  fetchDepartments,
  fetchUnassigned,
  removeDepartmentMember,
  updateDepartment,
  type DepartmentDetails,
  type DepartmentSummary,
} from './api'

type Props = {
  me: Me
  /** Something changed that may concern the signed-in person's own departments. */
  onChanged: () => void
}

/**
 * "Verwaltung" for organization admins and department leads (ADR 0021): departments and their people, who still waits
 * for a department and, for admins, erasing a person on request. Leads only see the departments they lead.
 */
export function AdminPage({ me, onChanged }: Props) {
  const admin = me.organizationRole === 'admin'
  const [departments, setDepartments] = useState<DepartmentSummary[] | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [unassigned, setUnassigned] = useState<User[]>([])
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchDepartments().then(
      (page) => {
        const manageable = page.items.filter((d) => d.canManage)
        setDepartments(manageable)
        setSelected((current) => current ?? manageable[0]?.id ?? null)
      },
      (e: Error) => setError(e.message),
    )
    fetchUnassigned().then(setUnassigned, () => setUnassigned([]))
  }, [])

  useEffect(load, [load])

  const changed = useCallback(() => {
    load()
    onChanged()
  }, [load, onChanged])

  return (
    <section className="admin" aria-labelledby="admin-heading">
      <header className="page-header">
        <h2 id="admin-heading">Verwaltung</h2>
        {admin && (
          <div className="page-actions">
            <AnonymizePersonForm myId={me.id} />
            <Reveal label="Abteilung" title="Neue Abteilung" primary>
              {(close) => (
                <CreateDepartmentForm
                  onCreated={(department) => {
                    close()
                    toast(`Abteilung „${department.name}“ angelegt.`)
                    setSelected(department.id)
                    load()
                  }}
                />
              )}
            </Reveal>
          </div>
        )}
      </header>
      {error && <p role="alert">{error}</p>}
      {unassigned.length > 0 && departments && departments.length > 0 && (
        <UnassignedPanel people={unassigned} departments={departments} onChanged={changed} />
      )}
      {departments === null ? (
        <Skeleton kind="tiles" label="Abteilungen werden geladen" />
      ) : departments.length === 0 ? (
        <EmptyState emoji="🏢" hint={admin ? 'Leg oben die erste Abteilung an.' : undefined}>
          Noch keine Abteilungen.
        </EmptyState>
      ) : (
        <div className="admin-layout">
          <ul className="department-list" aria-label="Abteilungen">
            {departments.map((department) => (
              <li key={department.id}>
                <button
                  type="button"
                  className={department.id === selected ? 'department selected' : 'department'}
                  aria-pressed={department.id === selected}
                  onClick={() => setSelected(department.id)}
                >
                  <span className="department-name">{department.name}</span>
                  <span className="department-count">{department.memberCount} Personen</span>
                </button>
              </li>
            ))}
          </ul>
          {selected && (
            <DepartmentPanel
              key={selected}
              departmentId={selected}
              admin={admin}
              onChanged={changed}
              onDeleted={() => {
                setSelected(null)
                changed()
              }}
            />
          )}
        </div>
      )}
    </section>
  )
}

function DepartmentPanel({ departmentId, admin, onChanged, onDeleted }: { departmentId: string; admin: boolean; onChanged: () => void; onDeleted: () => void }) {
  const [department, setDepartment] = useState<DepartmentDetails | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchDepartment(departmentId).then(setDepartment, (e: Error) => setError(e.message))
  }, [departmentId])

  useEffect(load, [load])

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
      load()
      onChanged()
      return true
    } catch (e) {
      setError((e as Error).message)
      load()
      return false
    }
  }

  async function remove() {
    if (!department || !window.confirm(`Abteilung „${department.name}“ löschen? Das geht nur, solange sie keine Projekte und kein Wissen hat.`)) return
    setError(null)
    try {
      await deleteDepartment(department.id)
      toast(`Abteilung „${department.name}“ gelöscht.`)
      onDeleted()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!department) return error ? <p role="alert">{error}</p> : <Skeleton label="Abteilung wird geladen" />

  return (
    <section className="panel department-details" aria-labelledby="department-heading">
      <h3 id="department-heading">
        <InlineEdit
          key={department.name}
          value={department.name}
          label="Name der Abteilung"
          editable={department.canManage}
          onSave={(name) => run(() => updateDepartment(department.id, department.version, { name }))}
        />
      </h3>
      <InlineEdit
        key={`d:${department.description ?? ''}`}
        value={department.description ?? ''}
        label="Beschreibung der Abteilung"
        editable={department.canManage}
        maxLength={2000}
        onSave={(description) => run(() => updateDepartment(department.id, department.version, { description: description || null }))}
      >
        {department.description || <span className="muted">Keine Beschreibung</span>}
      </InlineEdit>
      {error && <p role="alert">{error}</p>}
      {admin && <EntraGroupField department={department} onSave={(entraGroupId) => run(() => updateDepartment(department.id, department.version, { entraGroupId }))} />}
      <h4>Personen</h4>
      <ul className="plain-list">
        {department.members.map((member) => (
          <li key={member.userId} className="row">
            <span>
              {member.displayName}
              {member.source === 'entra' && <small className="muted"> · aus Entra</small>}
            </span>
            <span className="row">
              <select
                aria-label={`Rolle von ${member.displayName}`}
                value={member.role}
                onChange={(event) => void run(() => changeDepartmentMember(department.id, member.userId, event.target.value as DepartmentRole))}
              >
                <RoleOptions />
              </select>
              <button type="button" aria-label={`${member.displayName} entfernen`} onClick={() => void run(() => removeDepartmentMember(department.id, member.userId))}>
                <CloseIcon />
              </button>
            </span>
          </li>
        ))}
      </ul>
      <Reveal label="Person hinzufügen">
        {(close) => (
          <AddMemberForm
            excluded={department.members.map((m) => m.userId)}
            onAdd={async (userId, role) => {
              if (await run(() => addDepartmentMember(department.id, userId, role))) close()
            }}
          />
        )}
      </Reveal>
      {admin && (
        <p>
          <button type="button" className="link-button danger-link" onClick={() => void remove()}>
            Abteilung löschen
          </button>
        </p>
      )}
    </section>
  )
}

/** Organization admins connect a department to an Entra ID security group: its members join at sign-in. */
function EntraGroupField({ department, onSave }: { department: DepartmentDetails; onSave: (entraGroupId: string | null) => Promise<unknown> }) {
  const [value, setValue] = useState(department.entraGroupId ?? '')

  async function submit(event: FormEvent) {
    event.preventDefault()
    await onSave(value.trim() || null)
  }

  return (
    <form className="quick-create entra-group" onSubmit={(event) => void submit(event)}>
      <label>
        Entra-Gruppe (Objekt-ID)
        <input value={value} onChange={(event) => setValue(event.target.value)} maxLength={64} placeholder="z. B. 3f2a…, leer für keine" />
      </label>
      <button type="submit" disabled={value.trim() === (department.entraGroupId ?? '')}>
        Speichern
      </button>
      <small className="muted">Mitglieder der Gruppe kommen bei der Anmeldung automatisch in die Abteilung, wenn der Abgleich eingeschaltet ist.</small>
    </form>
  )
}

function UnassignedPanel({ people, departments, onChanged }: { people: User[]; departments: DepartmentSummary[]; onChanged: () => void }) {
  const [error, setError] = useState<string | null>(null)
  const [choice, setChoice] = useState<Record<string, string>>({})

  async function assign(person: User) {
    const departmentId = choice[person.id] ?? departments[0].id
    setError(null)
    try {
      await addDepartmentMember(departmentId, person.id, 'member')
      toast(`${person.displayName} ist jetzt in „${departments.find((d) => d.id === departmentId)?.name}“.`)
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel unassigned" aria-labelledby="unassigned-heading">
      <h3 id="unassigned-heading">Ohne Abteilung</h3>
      <p className="muted">Diese Personen haben sich angemeldet, gehören aber noch zu keiner Abteilung. Bis dahin sehen sie nur Projekte, zu denen sie eingeladen sind.</p>
      {error && <p role="alert">{error}</p>}
      <ul className="plain-list">
        {people.map((person) => (
          <li key={person.id} className="row">
            <span>
              {person.displayName} <small className="muted">{person.email}</small>
            </span>
            <span className="row">
              <select
                aria-label={`Abteilung für ${person.displayName}`}
                value={choice[person.id] ?? departments[0].id}
                onChange={(event) => setChoice({ ...choice, [person.id]: event.target.value })}
              >
                {departments.map((department) => (
                  <option key={department.id} value={department.id}>
                    {department.name}
                  </option>
                ))}
              </select>
              <button type="button" onClick={() => void assign(person)}>
                Aufnehmen
              </button>
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}

function RoleOptions() {
  return (
    <>
      {Object.entries(departmentRoles).map(([value, label]) => (
        <option key={value} value={value}>
          {label}
        </option>
      ))}
    </>
  )
}

function AddMemberForm({ excluded, onAdd }: { excluded: string[]; onAdd: (userId: string, role: DepartmentRole) => void }) {
  const [users, setUsers] = useState<User[]>([])
  const [userId, setUserId] = useState('')
  const [role, setRole] = useState<DepartmentRole>('member')

  useEffect(() => {
    fetchUsers().then((page) => setUsers(page.items), () => setUsers([]))
  }, [])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (userId) onAdd(userId, role)
  }

  return (
    <form className="stacked-form" onSubmit={submit}>
      <label>
        Person
        <select value={userId} onChange={(event) => setUserId(event.target.value)} required>
          <option value="">Bitte wählen</option>
          {users
            .filter((user) => !excluded.includes(user.id))
            .map((user) => (
              <option key={user.id} value={user.id}>
                {user.displayName}
              </option>
            ))}
        </select>
      </label>
      <label>
        Rolle
        <select value={role} onChange={(event) => setRole(event.target.value as DepartmentRole)}>
          <RoleOptions />
        </select>
      </label>
      <button type="submit">Hinzufügen</button>
    </form>
  )
}

function CreateDepartmentForm({ onCreated }: { onCreated: (department: DepartmentSummary) => void }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [entraGroupId, setEntraGroupId] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      onCreated(await createDepartment(name.trim(), description.trim(), entraGroupId.trim()))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form className="stacked-form" onSubmit={(event) => void submit(event)}>
      <label>
        Name der Abteilung
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} autoFocus />
      </label>
      <label>
        Beschreibung
        <input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={2000} />
      </label>
      <label>
        Entra-Gruppe (Objekt-ID, optional)
        <input value={entraGroupId} onChange={(event) => setEntraGroupId(event.target.value)} maxLength={64} />
      </label>
      <button type="submit">Abteilung anlegen</button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
