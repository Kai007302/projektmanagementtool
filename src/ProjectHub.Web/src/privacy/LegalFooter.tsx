import { useEffect, useState } from 'react'
import { downloadMyData, fetchLegal, type LegalInformation } from './api'

/** Privacy notice, imprint and the person's own data export, at the bottom of every page. */
export function LegalFooter({ signedIn }: { signedIn: boolean }) {
  const [legal, setLegal] = useState<LegalInformation | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // Without the links the app still works; the footer then only offers the export.
    fetchLegal().then(setLegal, () => {})
  }, [])

  async function download() {
    setError(null)
    try {
      await downloadMyData()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <nav className="legal-links" aria-label="Rechtliches">
      {legal?.privacyNoticeUrl && <a href={legal.privacyNoticeUrl}>Datenschutz</a>}
      {legal?.imprintUrl && <a href={legal.imprintUrl}>Impressum</a>}
      {signedIn && (
        <button type="button" className="link-button" onClick={() => void download()}>
          Meine Daten herunterladen
        </button>
      )}
      {error && <span role="alert">{error}</span>}
    </nav>
  )
}
