import { useState } from 'react'
import { CalendarFeedPanel } from '../calendar/CalendarFeedPanel'
import { downloadMyData } from '../privacy/api'
import { Dialog } from '../ui/Dialog'
import { Menu } from '../ui/Menu'
import type { Me } from './api'
import { initials } from './initials'

/**
 * The person's own things behind their avatar: their dates as a calendar and the export of their data (GDPR).
 * Both concern the person, not a project, so they live here and not on the overview page.
 */
export function AccountMenu({ me, role }: { me: Me; role: string }) {
  const [calendar, setCalendar] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function download() {
    setError(null)
    try {
      await downloadMyData()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <>
      <Menu
        className="account-menu"
        label={`Konto von ${me.displayName}`}
        trigger={
          <>
            <span className="avatar" aria-hidden="true">
              {initials(me.displayName)}
            </span>
            <span className="account-name">
              <strong>{me.displayName}</strong>
              <span>{role}</span>
            </span>
          </>
        }
        items={[
          { label: 'Meine Termine abonnieren', onSelect: () => setCalendar(true) },
          { label: 'Meine Daten herunterladen', onSelect: () => void download() },
        ]}
      />
      {error && <span role="alert">{error}</span>}
      {calendar && (
        <Dialog label="Meine Termine abonnieren" onClose={() => setCalendar(false)}>
          <CalendarFeedPanel />
        </Dialog>
      )}
    </>
  )
}
