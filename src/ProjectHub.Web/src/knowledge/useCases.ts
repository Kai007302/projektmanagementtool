import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import type { ArticleType, UseCaseKey } from './api'
import type { Block, InsightBlockField } from './blocks'

export type Tone = 'positive' | 'negative' | 'active' | 'neutral'

export type InsightField = { key: string; label: string; highlight: boolean }

export type InsightOutcome = { key: string; label: string; tone: Tone }

/** An article type that is an insight: the fields of its card and its possible results (ADR 0023). */
export type InsightKind = { articleType: ArticleType; label: string; pluralLabel: string; fields: InsightField[]; outcomes: InsightOutcome[] }

export type UseCase = { key: UseCaseKey; label: string; description: string; kinds: InsightKind[] }

let catalogue: Promise<UseCase[]> | null = null

/** The fixed use cases of knowledge spaces; they never change while the app runs, so they are loaded once. */
export function fetchUseCases(): Promise<UseCase[]> {
  catalogue ??= apiFetch<UseCase[]>('/api/v1/knowledge/use-cases').catch((error: unknown) => {
    catalogue = null
    throw error
  })
  return catalogue
}

/** The catalogue, or an empty list until it is loaded (or when it could not be loaded). */
export function useUseCases(): UseCase[] {
  const [useCases, setUseCases] = useState<UseCase[]>([])
  useEffect(() => {
    let current = true
    fetchUseCases().then(
      (value) => current && setUseCases(value),
      () => {},
    )
    return () => {
      current = false
    }
  }, [])
  return useCases
}

export const kindOf = (useCases: UseCase[], articleType: ArticleType): InsightKind | null =>
  useCases.flatMap((u) => u.kinds).find((k) => k.articleType === articleType) ?? null

export const findUseCase = (useCases: UseCase[], key: UseCaseKey | undefined): UseCase | null => useCases.find((u) => u.key === (key ?? 'general')) ?? null

export const outcomeOf = (kind: InsightKind | null, key: string | null | undefined): InsightOutcome | null =>
  kind?.outcomes.find((o) => o.key === key) ?? null

/** Tone colors on light ground: text and background pairs with at least 4.5:1 contrast. */
export const toneStyles: Record<Tone, { color: string; background: string }> = {
  positive: { color: '#166534', background: '#dcfce7' },
  negative: { color: '#991b1b', background: '#fee2e2' },
  active: { color: '#3730a3', background: '#e0e7ff' },
  neutral: { color: '#374151', background: '#f3f4f6' },
}

/** Planet colors in the galaxy: muted like the type colors, told apart by lightness as well. */
export const tonePlanetColors: Record<Tone, string> = {
  positive: '#4fb97f',
  negative: '#d5636b',
  active: '#d6ad6a',
  neutral: '#8f97ab',
}

export const toneLabels: Record<Tone, string> = { positive: 'bestätigt / gilt', negative: 'widerlegt / verworfen', active: 'läuft / offen', neutral: 'unklar / abgelöst' }

/**
 * The card fields of a kind filled from an existing card: texts are kept by key, so changing between kinds with the same
 * fields (e.g. "learning") keeps what was written; fields of the old kind that the new one lacks are kept at the end.
 */
export function cardFields(kind: InsightKind, current: InsightBlockField[] = []): InsightBlockField[] {
  const byKey = new Map(current.map((field) => [field.key, field]))
  const fields = kind.fields.map((field) => ({ key: field.key, label: field.label, text: byKey.get(field.key)?.text ?? '' }))
  const extra = current.filter((field) => !kind.fields.some((f) => f.key === field.key) && field.text.trim())
  return [...fields, ...extra]
}

/** Blocks with the card of <paramref name="kind"/> first: the existing card is updated, else a new one goes on top. */
export function withCard(blocks: Block[], kind: InsightKind | null): Block[] {
  const index = blocks.findIndex((block) => block.type === 'insight')
  if (!kind) return blocks
  if (index < 0) return [{ id: crypto.randomUUID().replaceAll('-', ''), type: 'insight', fields: cardFields(kind) }, ...blocks]
  const card = blocks[index]
  if (card.type !== 'insight') return blocks
  return blocks.map((block, i) => (i === index ? { ...card, fields: cardFields(kind, card.fields) } : block))
}
