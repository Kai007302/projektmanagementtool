const colors = ['#6366f1', '#ec4899', '#f59e0b', '#10b981', '#0ea5e9', '#a855f7']

/**
 * A short burst of confetti from a point on the screen (e.g. a card that was just completed).
 * Plain DOM with CSS animations; skipped when the person prefers reduced motion.
 */
export function celebrate(x = window.innerWidth / 2, y = window.innerHeight / 3) {
  if (typeof window === 'undefined' || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return
  const layer = document.createElement('div')
  layer.className = 'confetti'
  layer.setAttribute('aria-hidden', 'true')
  for (let i = 0; i < 48; i++) {
    const piece = document.createElement('span')
    const angle = Math.random() * Math.PI * 2
    const distance = 80 + Math.random() * 160
    piece.style.left = `${x}px`
    piece.style.top = `${y}px`
    piece.style.background = colors[i % colors.length]
    piece.style.setProperty('--dx', `${Math.cos(angle) * distance}px`)
    piece.style.setProperty('--dy', `${Math.sin(angle) * distance - 60}px`)
    piece.style.setProperty('--rot', `${Math.random() * 720 - 360}deg`)
    piece.style.animationDelay = `${Math.random() * 80}ms`
    layer.appendChild(piece)
  }
  document.body.appendChild(layer)
  window.setTimeout(() => layer.remove(), 1600)
}
