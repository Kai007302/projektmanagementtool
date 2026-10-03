import { useEffect, useState, type FormEvent } from 'react'
import type { User } from '../identity/api'
import { fetchUsers } from '../teams/api'
import { addProjectMember, changeProjectMember, projectRoles, removeProjectMember, type ProjectDetails, type ProjectRole } from './api'

type Props = { project: ProjectDetails; onChanged: () => void }

export function MembersPanel({ project, onChanged }: Props) {
  const [error, setError] = useState<string | null>(null)
  const canManage = project.capabilities.canManage

  async function run(action: () => Promise<void>) {
    setError(null)
    try {
      await action()
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel" aria-labelledby="members-heading">
      <h3 id="members-heading">Mitglieder</h3>
      {error && <p role="alert">{error}</p>}
      <ul className="plain-list">
        {project.members.map((member) => (
          <li key={member.userId} className="row">
            <span>{member.displayName}</span>
            {canManage ? (
              <span className="row">
                <select
                  aria-label={`Rolle von ${member.displayName}`}
                  value={member.role}
                  onChange={(event) =>
                    run(() => changeProjectMember(project.id, member.userId, event.target.value as ProjectRole))
                  }
                >
                  <RoleOptions />
                </select>
                <button
                  type="button"
                  aria-label={`${member.displayName} entfernen`}
                  onClick={() => run(() => removeProjectMember(project.id, member.userId))}
                >
                  ✕
                </button>
              </span>
            ) : (
              <small className="muted">{projectRoles[member.role]}</small>
            )}
          </li>
        ))}
      </ul>
      {canManage && (
        <AddMemberForm
          excluded={project.members.map((m) => m.userId)}
          onAdd={(userId, role) => run(() => addProjectMember(project.id, userId, role))}
        />
      )}
    </section>
  )
}

function RoleOptions() {
  return (
    <>
      {Object.entries(projectRoles).map(([value, label]) => (
        <option key={value} value={value}>
          {label}
        </option>
      ))}
    </>
  )
}

function AddMemberForm({ excluded, onAdd }: { excluded: string[]; onAdd: (userId: string, role: ProjectRole) => void }) {
  const [users, setUsers] = useState<User[]>([])
  const [userId, setUserId] = useState('')
  const [role, setRole] = useState<ProjectRole>('member')

  useEffect(() => {
    fetchUsers().then((page) => setUsers(page.items), () => setUsers([]))
  }, [])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!userId) return
    onAdd(userId, role)
    setUserId('')
  }

  return (
    <form className="stacked-form" onSubmit={submit}>
      <label>
        Person hinzufügen
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
        <select value={role} onChange={(event) => setRole(event.target.value as ProjectRole)}>
          <RoleOptions />
        </select>
      </label>
      <button type="submit">Hinzufügen</button>
    </form>
  )
}
