import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { createTeam, fetchTeams, type TeamSummary } from './api'
import { TeamDetailsPanel } from './TeamDetailsPanel'

export function TeamsPage({ me }: { me: Me }) {
  const [teams, setTeams] = useState<TeamSummary[] | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    fetchTeams().then(
      (page) => setTeams(page.items),
      (e: Error) => setError(e.message),
    )
  }, [])

  useEffect(load, [load])

  return (
    <section className="teams" aria-labelledby="teams-heading">
      <h2 id="teams-heading">Teams</h2>
      {error && <p role="alert">{error}</p>}
      {me.organizationRole === 'admin' && <CreateTeamForm onCreated={load} />}
      {teams === null ? (
        <p>Teams werden geladen …</p>
      ) : teams.length === 0 ? (
        <p>Noch keine Teams.</p>
      ) : (
        <ul className="team-list">
          {teams.map((team) => (
            <li key={team.id}>
              <button
                type="button"
                className={team.id === selected ? 'team selected' : 'team'}
                aria-pressed={team.id === selected}
                onClick={() => setSelected(team.id)}
              >
                <span className="team-name">{team.name}</span>
                <span className="team-count">{team.memberCount} Mitglieder</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {selected && <TeamDetailsPanel key={selected} teamId={selected} onChanged={load} />}
    </section>
  )
}

function CreateTeamForm({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await createTeam(name, description)
      setName('')
      setDescription('')
      onCreated()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form className="create-team" onSubmit={submit}>
      <label>
        Teamname
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
      </label>
      <label>
        Beschreibung
        <input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={2000} />
      </label>
      <button type="submit">Team anlegen</button>
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
