import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { User } from '../identity/api'
import { addTeamMember, fetchTeam, fetchUsers, removeTeamMember, type TeamDetails, type TeamMember } from './api'

const roleLabel: Record<TeamMember['role'], string> = { owner: 'Owner', member: 'Mitglied' }

export function TeamDetailsPanel({ teamId, onChanged }: { teamId: string; onChanged: () => void }) {
  const [team, setTeam] = useState<TeamDetails | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchTeam(teamId).then(setTeam, (e: Error) => setError(e.message))
  }, [teamId])

  useEffect(load, [load])

  async function run(action: () => Promise<void>) {
    setError(null)
    try {
      await action()
      load()
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!team) return error ? <p role="alert">{error}</p> : <p>Team wird geladen …</p>

  return (
    <div className="team-details">
      <h3>{team.name}</h3>
      {team.description && <p>{team.description}</p>}
      {error && <p role="alert">{error}</p>}
      <ul className="member-list">
        {team.members.map((member) => (
          <li key={member.userId}>
            <span>
              {member.displayName} <small>{roleLabel[member.role]}</small>
            </span>
            {team.canManage && (
              <button type="button" onClick={() => run(() => removeTeamMember(team.id, member.userId))}>
                Entfernen
              </button>
            )}
          </li>
        ))}
      </ul>
      {team.canManage && (
        <AddMemberForm
          excluded={team.members.map((m) => m.userId)}
          onAdd={(userId, role) => run(() => addTeamMember(team.id, userId, role))}
        />
      )}
    </div>
  )
}

function AddMemberForm({
  excluded,
  onAdd,
}: {
  excluded: string[]
  onAdd: (userId: string, role: TeamMember['role']) => void
}) {
  const [users, setUsers] = useState<User[]>([])
  const [userId, setUserId] = useState('')
  const [role, setRole] = useState<TeamMember['role']>('member')

  useEffect(() => {
    fetchUsers().then((page) => setUsers(page.items), () => setUsers([]))
  }, [])

  const candidates = users.filter((user) => !excluded.includes(user.id))

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!userId) return
    onAdd(userId, role)
    setUserId('')
  }

  return (
    <form className="add-member" onSubmit={submit}>
      <label>
        Person
        <select value={userId} onChange={(event) => setUserId(event.target.value)} required>
          <option value="">Bitte wählen</option>
          {candidates.map((user) => (
            <option key={user.id} value={user.id}>
              {user.displayName}
            </option>
          ))}
        </select>
      </label>
      <label>
        Rolle
        <select value={role} onChange={(event) => setRole(event.target.value as TeamMember['role'])}>
          <option value="member">Mitglied</option>
          <option value="owner">Owner</option>
        </select>
      </label>
      <button type="submit">Hinzufügen</button>
    </form>
  )
}
