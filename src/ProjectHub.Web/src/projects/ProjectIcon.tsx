import { useEffect, useState } from 'react'
import { apiBlob } from '../api/client'
import { projectLook } from '../ui/personality'

type Props = { project: { id: string; icon?: string | null; logoVersion?: number | null }; className?: string }

/** Logos as object URLs, once per project and logo version, so lists and headers share one request. */
const logos = new Map<string, Promise<string>>()

function logoUrl(projectId: string, version: number) {
  const key = `${projectId}:${version}`
  let url = logos.get(key)
  if (!url) {
    url = apiBlob(`/api/v1/projects/${projectId}/logo?v=${version}`).then((blob) => URL.createObjectURL(blob))
    // A failed load is tried again next time.
    url.catch(() => logos.delete(key))
    logos.set(key, url)
  }
  return url
}

/** The project's logo if it has one, otherwise its emoji: the chosen one, or a stable one from the id. */
export function ProjectIcon({ project, className = 'project-icon' }: Props) {
  const [logo, setLogo] = useState<{ key: string; url: string } | null>(null)
  const key = project.logoVersion ? `${project.id}:${project.logoVersion}` : null

  useEffect(() => {
    if (!key || !project.logoVersion) return
    let current = true
    logoUrl(project.id, project.logoVersion).then(
      (url) => current && setLogo({ key, url }),
      () => {},
    )
    return () => {
      current = false
    }
  }, [key, project.id, project.logoVersion])

  return (
    <span className={className} aria-hidden="true">
      {logo && logo.key === key ? <img src={logo.url} alt="" /> : (project.icon ?? projectLook(project.id).emoji)}
    </span>
  )
}
