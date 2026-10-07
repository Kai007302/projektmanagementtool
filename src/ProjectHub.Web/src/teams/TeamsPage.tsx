import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Me } from '../identity/api'
import { AnonymizePersonForm } from '../privacy/AnonymizePersonForm'
import { createTeam, fetchTeams, type TeamSummary } from './api'
import { TeamDetailsPanel } from './TeamDetailsPanel'
import { EmptyState } from '../ui/EmptyState'
import { Reveal } from '../ui/Reveal'

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
      <header className="page-header">
        <h2 id="teams-heading">Teams</h2>
        {me.organizationRole === 'admin' && (
          <div className="page-actions">
            <AnonymizePersonForm myId={me.id} />
            <Reveal label="Team" title="Neues Team" primary>
              {(close) => (
                <CreateTeamForm
                  onCreated={() => {
                    close()
                    load()
                  }}
                />
              )}
            </Reveal>
          </div>
        )}
      </header>
      {error && <p role="alert">{error}</p>}
      {teams === null ? (
        <p>Teams werden geladen …</p>
      ) : teams.length === 0 ? (
        <EmptyState emoji="👥" hint="Leg oben das erste Team an.">
          Noch keine Teams.
        </EmptyState>
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
    <form className="stacked-form" onSubmit={submit}>
      <label>
        Teamname
        <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} autoFocus />
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
