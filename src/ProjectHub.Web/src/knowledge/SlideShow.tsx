import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { articleTypeEmoji, articleTypes, type ArticleDetails } from './api'
import { BlockView } from './BlockView'
import { DataTableView } from './DataTableView'
import { Confidence, OutcomeBadge } from './Insight'
import { buildSlides, type Slide } from './slides'
import type { InsightKind } from './useCases'
import { CloseIcon } from '../ui/icons'
import './insights.css'

type Props = { details: ArticleDetails; kind: InsightKind | null; onClose: () => void; onOpenArticle?: (id: string) => void }

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'long' })

/**
 * The article as a presentation instead of a slide deck (ADR 0023): full window, arrow keys or the buttons to move, Esc to
 * close. The slides are built from the article each time, so they always show its current state.
 */
export function SlideShow({ details, kind, onClose, onOpenArticle }: Props) {
  const slides = useMemo(() => buildSlides(details.content.blocks, kind), [details.content.blocks, kind])
  const [index, setIndex] = useState(0)
  const dialog = useRef<HTMLDivElement>(null)
  const { article } = details

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    dialog.current?.focus()
    return () => {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
      previous?.focus?.()
    }
  }, [])

  const go = (next: number) => setIndex(Math.max(0, Math.min(slides.length - 1, next)))

  function onKeyDown(event: KeyboardEvent) {
    const keys: Record<string, () => void> = {
      ArrowRight: () => go(index + 1),
      PageDown: () => go(index + 1),
      ' ': () => go(index + 1),
      ArrowLeft: () => go(index - 1),
      PageUp: () => go(index - 1),
      Home: () => go(0),
      End: () => go(slides.length - 1),
      Escape: onClose,
    }
    const action = keys[event.key]
    // Space inside a button presses the button; only the slide itself turns the page with it.
    if (!action || (event.key === ' ' && event.target !== dialog.current)) return
    event.preventDefault()
    action()
  }

  function fullscreen() {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
    else dialog.current?.requestFullscreen?.().catch(() => {})
  }

  const slide = slides[index]
  return createPortal(
    <div className="slideshow" role="dialog" aria-modal="true" aria-label={`Folien: ${article.title}`} tabIndex={-1} ref={dialog} onKeyDown={onKeyDown}>
      <div className="slide-bar">
        <span className="slide-context">
          <span aria-hidden="true">{articleTypeEmoji[article.articleType]}</span> {articleTypes[article.articleType]} · {article.title}
        </span>
        <span className="slide-progress" aria-hidden="true">
          {slides.map((_, i) => (
            <span key={i} className={i === index ? 'on' : i < index ? 'done' : ''} />
          ))}
        </span>
        <button type="button" onClick={fullscreen}>
          Vollbild
        </button>
        <button type="button" className="icon-button" aria-label="Folien schließen" onClick={onClose}>
          <CloseIcon />
        </button>
      </div>
      <section className={`slide slide-${slide.type}${slide.type === 'field' && slide.highlight ? ' slide-result' : ''}`} aria-live="polite" aria-label={`Folie ${index + 1} von ${slides.length}`}>
        <SlideContent slide={slide} details={details} kind={kind} onOpenArticle={onOpenArticle} />
      </section>
      <div className="slide-nav">
        <button type="button" onClick={() => go(index - 1)} disabled={index === 0}>
          ← Zurück
        </button>
        <span className="muted">
          {index + 1} / {slides.length}
        </span>
        <button type="button" onClick={() => go(index + 1)} disabled={index === slides.length - 1}>
          Weiter →
        </button>
      </div>
    </div>,
    document.body,
  )
}

function SlideContent({ slide, details, kind, onOpenArticle }: { slide: Slide; details: ArticleDetails; kind: InsightKind | null; onOpenArticle?: (id: string) => void }) {
  const { article } = details
  switch (slide.type) {
    case 'title':
      return (
        <>
          <p className="slide-kicker">
            {articleTypes[article.articleType]}
            {article.spaceName && ` · ${article.spaceName}`}
          </p>
          <h2 className="slide-title">{article.title}</h2>
          {kind && (
            <p className="slide-meta">
              <OutcomeBadge kind={kind} outcome={article.outcome} /> <Confidence value={article.confidence} />
            </p>
          )}
          {article.summary && <p className="slide-lead">{article.summary}</p>}
          <p className="muted">
            {article.ownerName && `${article.ownerName} · `}Stand {dateFormat.format(new Date(article.updatedAt))}
          </p>
        </>
      )
    case 'field':
      return (
        <>
          <p className="slide-kicker">{slide.field.label}</p>
          <p className={slide.highlight ? 'slide-statement highlight' : 'slide-statement'}>{slide.field.text}</p>
          {slide.highlight && kind && (
            <p className="slide-meta">
              <OutcomeBadge kind={kind} outcome={article.outcome} /> <Confidence value={article.confidence} />
            </p>
          )}
        </>
      )
    case 'section':
      return (
        <>
          {slide.heading && <h2 className="slide-heading">{slide.heading}</h2>}
          <BlockView blocks={slide.blocks} onOpenArticle={onOpenArticle} kind={kind} />
        </>
      )
    case 'data':
      return (
        <>
          {slide.block.title && <h2 className="slide-heading">{slide.block.title}</h2>}
          <DataTableView columns={slide.block.columns} rows={slide.block.rows} chart={slide.block.chart} large />
        </>
      )
  }
}
