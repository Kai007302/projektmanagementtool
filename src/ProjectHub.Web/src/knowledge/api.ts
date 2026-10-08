import { apiFetch, jsonBody, type Paged } from '../api/client'
import type { Block } from './blocks'

export type ArticleType = 'article' | 'how_to' | 'best_practice' | 'process' | 'policy' | 'faq' | 'template' | 'checklist' | 'glossary'
export type ArticleStatus = 'draft' | 'review' | 'published' | 'archived'
export type Visibility = 'organization' | 'department' | 'restricted'
export type Grant = 'view' | 'edit' | 'admin'
export type RelationType = 'RELATED' | 'REQUIRES' | 'PART_OF' | 'SUPERSEDES' | 'REFERENCES'
export type ResourceType = 'project' | 'task' | 'department' | 'whiteboard'

export const articleTypes: Record<ArticleType, string> = {
  article: 'Artikel',
  how_to: 'Anleitung',
  best_practice: 'Best Practice',
  process: 'Prozess',
  policy: 'Richtlinie',
  faq: 'FAQ',
  template: 'Vorlage',
  checklist: 'Checkliste',
  glossary: 'Glossar',
}

/** Icon per article type, shown next to the type everywhere (decorative; the label stays). */
export const articleTypeEmoji: Record<ArticleType, string> = {
  article: '📄',
  how_to: '📘',
  best_practice: '⭐',
  process: '🔄',
  policy: '📜',
  faq: '❓',
  template: '🧾',
  checklist: '✅',
  glossary: '🔤',
}

export const articleStatuses: Record<ArticleStatus, string> = {
  draft: 'Entwurf',
  review: 'In Prüfung',
  published: 'Veröffentlicht',
  archived: 'Archiviert',
}

/** Published articles are read by these people (ADR 0021); drafts only by their author and those with a grant. */
export const visibilities: Record<Visibility, string> = {
  department: 'Abteilung (sobald veröffentlicht)',
  organization: 'Ganze Organisation (sobald veröffentlicht)',
  restricted: 'Nur freigegebene Personen und Abteilungen',
}

export const grants: Record<Grant, string> = { view: 'Lesen', edit: 'Bearbeiten', admin: 'Verwalten' }

export const relationTypes: Record<RelationType, { outgoing: string; incoming: string }> = {
  RELATED: { outgoing: 'Verwandt mit', incoming: 'Verwandt mit' },
  REQUIRES: { outgoing: 'Setzt voraus', incoming: 'Ist Voraussetzung für' },
  PART_OF: { outgoing: 'Teil von', incoming: 'Enthält' },
  SUPERSEDES: { outgoing: 'Ersetzt', incoming: 'Ersetzt durch' },
  REFERENCES: { outgoing: 'Verweist auf', incoming: 'Referenziert von' },
}

export const resourceTypes: Record<ResourceType, string> = { project: 'Projekt', task: 'Aufgabe', department: 'Abteilung', whiteboard: 'Whiteboard' }

export type ArticleSummary = {
  id: string
  title: string
  slug: string
  articleType: ArticleType
  summary: string | null
  status: ArticleStatus
  visibility: Visibility
  spaceId: string | null
  spaceName: string | null
  ownerId: string | null
  ownerName: string | null
  tags: string[]
  updatedAt: string
  publishedAt: string | null
  version: number
  departmentId?: string | null
  departmentName?: string | null
  /** The first 25 words of the summary or the text, for overview cards. */
  excerpt?: string | null
}

export type Relation = { id: string; relationType: RelationType; direction: 'outgoing' | 'incoming'; articleId: string; title: string; articleType: ArticleType }

export type Reference = { id: string; resourceType: ResourceType; resourceId: string; title: string; projectId: string | null }

export type ArticleDetails = {
  article: ArticleSummary
  content: { blocks: Block[] }
  versionNumber: number
  reviewDueAt: string | null
  /** canShareWithOrganization: organization admins and leads of the article's department (ADR 0021). */
  capabilities: { canEdit: boolean; canAdmin: boolean; canShareWithOrganization?: boolean }
  relations: Relation[]
  references: Reference[]
}

export type VersionSummary = { id: string; versionNumber: number; createdBy: string; createdByName: string | null; changeNote: string | null; createdAt: string }

export type Space = { id: string; name: string; description: string | null; articleCount: number; version: number; departmentId?: string | null }

export type Tag = { name: string; articleCount: number }

export type Permission = { principalType: 'user' | 'department'; principalId: string; name: string; permission: Grant }

export type KnowledgeComment = {
  id: string
  articleId: string
  authorId: string
  authorName: string
  content: string
  createdAt: string
  updatedAt: string
  version: number
}

export type ArticleFilter = { q?: string; type?: string; status?: string; spaceId?: string; tag?: string; departmentId?: string }

const base = '/api/v1/knowledge'

export function searchArticles(filter: ArticleFilter) {
  const query = new URLSearchParams({ limit: '100' })
  for (const [key, value] of Object.entries(filter)) if (value) query.set(key, value)
  return apiFetch<Paged<ArticleSummary>>(`${base}/articles?${query}`)
}

export const fetchArticle = (id: string) => apiFetch<ArticleDetails>(`${base}/articles/${id}`)

export type NewArticle = {
  title: string
  articleType: ArticleType
  summary: string
  spaceId: string | null
  visibility: Visibility
  /** Without one the article goes to the space's department, else my first department. */
  departmentId?: string | null
}

export const createArticle = (article: NewArticle) => apiFetch<ArticleDetails>(`${base}/articles`, { method: 'POST', body: jsonBody(article) })

export type ArticleChanges = Partial<{ title: string; summary: string | null; articleType: ArticleType; spaceId: string | null; visibility: Visibility }>

export const updateArticle = (id: string, version: number, changes: ArticleChanges) =>
  apiFetch<ArticleDetails>(`${base}/articles/${id}`, { method: 'PATCH', body: jsonBody({ version, ...changes }) })

export const deleteArticle = (id: string) => apiFetch<void>(`${base}/articles/${id}`, { method: 'DELETE' })

export const saveContent = (id: string, version: number, blocks: Block[], changeNote: string) =>
  apiFetch<ArticleDetails>(`${base}/articles/${id}/content`, { method: 'PUT', body: jsonBody({ version, content: { blocks }, changeNote }) })

export const changeStatus = (id: string, version: number, status: ArticleStatus) =>
  apiFetch<ArticleDetails>(`${base}/articles/${id}/status`, { method: 'POST', body: jsonBody({ version, status }) })

export const fetchVersions = (id: string) => apiFetch<VersionSummary[]>(`${base}/articles/${id}/versions`)

export const restoreVersion = (id: string, number: number, version: number) =>
  apiFetch<ArticleDetails>(`${base}/articles/${id}/versions/${number}/restore`, { method: 'POST', body: jsonBody({ version }) })

export const setTags = (id: string, tags: string[]) => apiFetch<ArticleDetails>(`${base}/articles/${id}/tags`, { method: 'PUT', body: jsonBody({ tags }) })

export const fetchTags = () => apiFetch<Tag[]>(`${base}/tags`)

/** The knowledge spaces I can see; with a department only those of that department. */
export const fetchSpaces = (departmentId = '') => apiFetch<Space[]>(`${base}/spaces${departmentId ? `?departmentId=${departmentId}` : ''}`)

export const createSpace = (name: string, description: string, departmentId = '') =>
  apiFetch<Space>(`${base}/spaces`, { method: 'POST', body: jsonBody({ name, description, departmentId: departmentId || null }) })

export const fetchPermissions = (id: string) => apiFetch<Permission[]>(`${base}/articles/${id}/permissions`)

export const setPermissions = (id: string, permissions: Pick<Permission, 'principalType' | 'principalId' | 'permission'>[]) =>
  apiFetch<Permission[]>(`${base}/articles/${id}/permissions`, { method: 'PUT', body: jsonBody({ permissions }) })

export const addRelation = (id: string, targetArticleId: string, relationType: RelationType) =>
  apiFetch<Relation>(`${base}/articles/${id}/relations`, { method: 'POST', body: jsonBody({ targetArticleId, relationType }) })

export const removeRelation = (relationId: string) => apiFetch<void>(`${base}/relations/${relationId}`, { method: 'DELETE' })

export const addReference = (id: string, resourceType: ResourceType, resourceId: string) =>
  apiFetch<Reference>(`${base}/articles/${id}/references`, { method: 'POST', body: jsonBody({ resourceType, resourceId }) })

export const removeReference = (referenceId: string) => apiFetch<void>(`${base}/references/${referenceId}`, { method: 'DELETE' })

export const fetchKnowledgeComments = (id: string) => apiFetch<Paged<KnowledgeComment>>(`${base}/articles/${id}/comments?limit=100`)

export const addKnowledgeComment = (id: string, content: string, mentionedUserIds: string[]) =>
  apiFetch<KnowledgeComment>(`${base}/articles/${id}/comments`, { method: 'POST', body: jsonBody({ content, mentionedUserIds }) })

export const deleteKnowledgeComment = (commentId: string) => apiFetch<void>(`${base}/comments/${commentId}`, { method: 'DELETE' })

/** Visible articles that reference a project, task or department. */
export const fetchLinkedKnowledge = (resourceType: ResourceType, id: string) =>
  apiFetch<ArticleSummary[]>(`/api/v1/${resourceType}s/${id}/knowledge`)
