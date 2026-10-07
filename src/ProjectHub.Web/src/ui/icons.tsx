import type { ReactNode } from 'react'

/**
 * Line icons for buttons and controls, 16 × 16 by default, in the current text color (like the whiteboard tools).
 * Emoji stay for content with personality: priorities, project symbols, article types, empty states.
 */
function LineIcon({ size = 16, children }: { size?: number; children: ReactNode }) {
  return (
    <svg className="line-icon" viewBox="0 0 20 20" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

export const CloseIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <path d="M5 5 L15 15 M15 5 L5 15" />
  </LineIcon>
)

export const BellIcon = ({ size = 18 }: { size?: number }) => (
  <LineIcon size={size}>
    <path d="M5 13.5 V9 a5 5 0 0 1 10 0 V13.5 L16.5 15 H3.5 Z" />
    <path d="M8.25 17.25 a1.9 1.9 0 0 0 3.5 0" />
  </LineIcon>
)

export const SearchIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <circle cx="9" cy="9" r="5.5" />
    <path d="M13.2 13.2 L17 17" />
  </LineIcon>
)

export const CalendarIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <rect x="3" y="4.5" width="14" height="12.5" rx="2" />
    <path d="M3 8.5 H17 M7 2.5 V6.5 M13 2.5 V6.5" />
  </LineIcon>
)

export const CheckIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <path d="M4.5 10.5 L8.5 14.5 L15.5 6" />
  </LineIcon>
)

export const FolderIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <path d="M2.5 6 a1.5 1.5 0 0 1 1.5 -1.5 H8 L10 6.5 H16 a1.5 1.5 0 0 1 1.5 1.5 V14.5 a1.5 1.5 0 0 1 -1.5 1.5 H4 a1.5 1.5 0 0 1 -1.5 -1.5 Z" />
  </LineIcon>
)

export const DocumentIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <path d="M5 2.5 H11.5 L15 6 V17.5 H5 Z M11.5 2.5 V6 H15 M7.5 10 H12.5 M7.5 13 H12.5" />
  </LineIcon>
)

export const WhiteboardIcon = ({ size }: { size?: number }) => (
  <LineIcon size={size}>
    <rect x="2.5" y="3.5" width="15" height="10.5" rx="1.5" />
    <path d="M10 14 V17 M6.5 17 H13.5 M6 10.5 L9 7.5 L11 9.5 L14 6.5" />
  </LineIcon>
)
