import { useState, type FormEvent } from 'react'
import type { User } from '../identity/api'
import { Reveal } from '../ui/Reveal'
import { anonymizeUser, searchUsers } from './api'

/**
 * For organization admins: erases a person on request (Art. 17 DSGVO, ADR 0017). Name, e-mail and memberships are
 * removed; tasks, comments and articles stay with "Ehemalige Person" as author.
 */
export function AnonymizePersonForm({ myId }: { myId: string }) {
  const [search, setSearch] = useState('')
  const [results, setResults] = useState<User[] | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function find(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setMessage(null)
    try {
      setResults((await searchUsers(search.trim())).items.filter((user) => user.id !== myId))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  async function anonymize(user: User) {
    const question =
      `${user.displayName} (${user.email}) wirklich anonymisieren?\n\n` +
      'Name, E-Mail-Adresse, Mitgliedschaften und Benachrichtigungen werden gelöscht, zugewiesene Aufgaben werden frei. ' +
      'Das lässt sich nicht rückgängig machen.'
    if (!window.confirm(question)) return
    setError(null)
    try {
      await anonymizeUser(user.id)
      setResults((current) => current?.filter((item) => item.id !== user.id) ?? null)
      setMessage(`${user.displayName} wurde anonymisiert.`)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <Reveal label="Person anonymisieren" title="Person anonymisieren (Löschung nach DSGVO)" plus={false}>
      {() => (
        <div className="anonymize-person">
          <form onSubmit={find}>
            <label>
              Name oder E-Mail
              <input value={search} onChange={(event) => setSearch(event.target.value)} required maxLength={200} />
            </label>
            <button type="submit">Suchen</button>
          </form>
          {results !== null &&
            (results.length === 0 ? (
              <p>Niemand gefunden.</p>
            ) : (
              <ul>
                {results.map((user) => (
                  <li key={user.id}>
                    <span>
                      {user.displayName} · {user.email}
                    </span>
                    <button type="button" className="danger" onClick={() => void anonymize(user)}>
                      Anonymisieren
                    </button>
                  </li>
                ))}
              </ul>
            ))}
          {message && <p role="status">{message}</p>}
          {error && <p role="alert">{error}</p>}
        </div>
      )}
    </Reveal>
  )
}
