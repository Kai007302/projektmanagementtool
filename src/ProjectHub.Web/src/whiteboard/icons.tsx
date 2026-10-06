import type { ReactNode } from 'react'

/** Line icons for the whiteboard tools, 20 × 20, in the current text color. */
function Icon({ children }: { children: ReactNode }) {
  return (
    <svg className="board-icon" viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

export const RectIcon = () => (
  <Icon>
    <rect x="3" y="5" width="14" height="10" rx="1.5" />
  </Icon>
)

export const EllipseIcon = () => (
  <Icon>
    <ellipse cx="10" cy="10" rx="7" ry="5.5" />
  </Icon>
)

export const DiamondIcon = () => (
  <Icon>
    <path d="M10 3 L17 10 L10 17 L3 10 Z" />
  </Icon>
)

export const TextIcon = () => (
  <Icon>
    <path d="M4 5 V3.5 H16 V5 M10 3.5 V16.5 M7.5 16.5 H12.5" />
  </Icon>
)

export const ArrowIcon = () => (
  <Icon>
    <path d="M4 16 L16 4 M9 4 H16 V11" />
  </Icon>
)

export const TaskIcon = () => (
  <Icon>
    <rect x="3" y="3" width="14" height="14" rx="3" />
    <path d="M6.5 10 L9 12.5 L13.5 7.5" />
  </Icon>
)

export const TemplatesIcon = () => (
  <Icon>
    <rect x="3" y="3" width="6" height="6" rx="1" />
    <rect x="11" y="3" width="6" height="6" rx="1" />
    <rect x="3" y="11" width="6" height="6" rx="1" />
    <rect x="11" y="11" width="6" height="6" rx="1" />
  </Icon>
)

export const UndoIcon = () => (
  <Icon>
    <path d="M7 5 L3.5 8.5 L7 12 M3.5 8.5 H12 A4.5 4.5 0 0 1 12 17.5 H8" />
  </Icon>
)

export const RedoIcon = () => (
  <Icon>
    <path d="M13 5 L16.5 8.5 L13 12 M16.5 8.5 H8 A4.5 4.5 0 0 0 8 17.5 H12" />
  </Icon>
)

export const MinusIcon = () => (
  <Icon>
    <path d="M5 10 H15" />
  </Icon>
)

export const PlusIcon = () => (
  <Icon>
    <path d="M5 10 H15 M10 5 V15" />
  </Icon>
)

export const FitIcon = () => (
  <Icon>
    <path d="M3 7 V3 H7 M13 3 H17 V7 M17 13 V17 H13 M7 17 H3 V13" />
  </Icon>
)

export const PencilIcon = () => (
  <Icon>
    <path d="M4 16 L4.8 12.6 L13 4.4 a1.6 1.6 0 0 1 2.3 0 l0.3 0.3 a1.6 1.6 0 0 1 0 2.3 L7.4 15.2 Z" />
    <path d="M11.8 5.6 L14.4 8.2" />
  </Icon>
)

export const TrashIcon = () => (
  <Icon>
    <path d="M4 6 H16" />
    <path d="M8 6 V4.5 H12 V6" />
    <path d="M5.5 6 L6.3 16 H13.7 L14.5 6" />
  </Icon>
)

export const OpenIcon = () => (
  <Icon>
    <path d="M11 4 H16 V9" />
    <path d="M16 4 L9.5 10.5" />
    <path d="M14 12 V16 H4 V6 H8" />
  </Icon>
)

export const ListIcon = () => (
  <Icon>
    <path d="M7 5.5 H16 M7 10 H16 M7 14.5 H16" />
    <circle cx="4" cy="5.5" r="0.6" fill="currentColor" />
    <circle cx="4" cy="10" r="0.6" fill="currentColor" />
    <circle cx="4" cy="14.5" r="0.6" fill="currentColor" />
  </Icon>
)

export const LockIcon = () => (
  <Icon>
    <rect x="4.5" y="9" width="11" height="7.5" rx="1.5" />
    <path d="M7 9 V6.5 a3 3 0 0 1 6 0 V9" />
  </Icon>
)

export const UnlockIcon = () => (
  <Icon>
    <rect x="4.5" y="9" width="11" height="7.5" rx="1.5" />
    <path d="M7 9 V6.5 a3 3 0 0 1 5.8 -1" />
  </Icon>
)

export const ConnectIcon = () => (
  <Icon>
    <circle cx="4.5" cy="15.5" r="2" />
    <circle cx="15.5" cy="4.5" r="2" />
    <path d="M6.2 13.8 L13.8 6.2" />
  </Icon>
)
