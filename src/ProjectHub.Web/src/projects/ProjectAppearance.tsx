import { useId, useRef, useState } from 'react'
import { deleteProjectLogo, updateProject, uploadProjectLogo, type ProjectSummary } from './api'
import { ProjectIcon } from './ProjectIcon'

/** A choice of symbols; a project without one shows a symbol derived from its id. */
const emojis = ['🚀', '🌱', '🧭', '🛠️', '🎯', '💡', '📦', '🌍', '🧩', '⚡', '🏗️', '🔭', '📈', '🧪', '🏥', '🏦', '🚚', '🎓', '🔒', '☁️', '📣', '🤝', '🏆', '❤️']

const maxLogoBytes = 256 * 1024

type Props = {
  project: Pick<ProjectSummary, 'id' | 'name' | 'version' | 'icon' | 'logoVersion'>
  onChanged: () => void
}

/** Symbol and logo of a project, opened from the project's menu. Every choice is saved at once. */
export function ProjectAppearance({ project, onChanged }: Props) {
  const [current, setCurrent] = useState(project)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const inputId = useId()

  async function run(action: () => Promise<Partial<typeof project>>) {
    setError(null)
    setBusy(true)
    try {
      const changes = await action()
      setCurrent((before) => ({ ...before, ...changes }))
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const chooseIcon = (icon: string | null) =>
    run(async () => {
      const saved = await updateProject(current.id, current.version, { icon })
      return { icon: saved.icon ?? null, version: saved.version }
    })

  function upload(file: File | undefined) {
    if (!file) return
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
      setError('Bitte ein Bild als PNG, JPEG oder WebP wählen.')
      return
    }
    if (file.size > maxLogoBytes) {
      setError('Das Logo darf höchstens 256 KB groß sein.')
      return
    }
    void run(async () => ({ logoVersion: (await uploadProjectLogo(current.id, file)).logoVersion }))
  }

  return (
    <section className="project-appearance" aria-labelledby="project-appearance-heading">
      <h3 id="project-appearance-heading">Symbol und Logo</h3>
      <div className="project-appearance-preview">
        <ProjectIcon project={current} className="project-card-icon" />
        <strong>{current.name}</strong>
      </div>
      <h4>Symbol</h4>
      <div className="emoji-grid" role="group" aria-label="Symbol">
        {emojis.map((emoji) => (
          <button key={emoji} type="button" className="emoji-choice" aria-pressed={current.icon === emoji} disabled={busy} onClick={() => void chooseIcon(emoji)}>
            {emoji}
          </button>
        ))}
      </div>
      {current.icon && (
        <button type="button" className="link-button" disabled={busy} onClick={() => void chooseIcon(null)}>
          Symbol zurücksetzen
        </button>
      )}
      <h4>Logo</h4>
      <p className="muted">Ein Logo ersetzt das Symbol. PNG, JPEG oder WebP, höchstens 256 KB, am besten quadratisch.</p>
      <div className="row">
        <label htmlFor={inputId} className="visually-hidden">
          Logo-Datei
        </label>
        <input
          id={inputId}
          ref={fileInput}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          className="visually-hidden"
          onChange={(event) => {
            upload(event.target.files?.[0])
            event.target.value = ''
          }}
        />
        <button type="button" disabled={busy} onClick={() => fileInput.current?.click()}>
          {current.logoVersion ? 'Anderes Logo hochladen' : 'Logo hochladen'}
        </button>
        {current.logoVersion && (
          <button type="button" className="danger" disabled={busy} onClick={() => void run(async () => ({ logoVersion: (await deleteProjectLogo(current.id)).logoVersion }))}>
            Logo entfernen
          </button>
        )}
      </div>
      {error && <p role="alert">{error}</p>}
    </section>
  )
}
