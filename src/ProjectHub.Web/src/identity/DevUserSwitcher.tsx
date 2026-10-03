import { useEffect, useState } from 'react'
import { fetchDevUsers, type DevUser } from './api'

type Props = {
  current: string | null
  onChange: (objectId: string) => void
}

/** Development sign-in: pick one of the synthetic users. Not rendered in production builds. */
export function DevUserSwitcher({ current, onChange }: Props) {
  const [users, setUsers] = useState<DevUser[]>([])

  useEffect(() => {
    fetchDevUsers().then(setUsers, () => setUsers([]))
  }, [])

  if (users.length === 0) return null

  return (
    <label className="dev-switcher">
      Dev-Anmeldung als
      <select value={current ?? ''} onChange={(event) => onChange(event.target.value)}>
        <option value="" disabled>
          Standardbenutzer
        </option>
        {users.map((user) => (
          <option key={user.objectId} value={user.objectId}>
            {user.displayName} ({user.organization}
            {user.organizationRole === 'admin' ? ', Admin' : ''})
          </option>
        ))}
      </select>
    </label>
  )
}
