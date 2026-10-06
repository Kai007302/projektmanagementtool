import { useEffect, useState } from 'react'
import { fetchLegal, type LegalInformation } from './api'

/**
 * Privacy notice and imprint at the bottom of every page. The person's own data export and calendar are in the menu
 * behind their avatar, the calendar of a project in that project's menu.
 */
export function LegalFooter() {
  const [legal, setLegal] = useState<LegalInformation | null>(null)

  useEffect(() => {
    // Without the links the app still works; the footer then stays empty.
    fetchLegal().then(setLegal, () => {})
  }, [])

  if (!legal?.privacyNoticeUrl && !legal?.imprintUrl) return null

  return (
    <nav className="legal-links" aria-label="Rechtliches">
      {legal.privacyNoticeUrl && <a href={legal.privacyNoticeUrl}>Datenschutz</a>}
      {legal.imprintUrl && <a href={legal.imprintUrl}>Impressum</a>}
    </nav>
  )
}
