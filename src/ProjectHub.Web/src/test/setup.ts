import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  localStorage.clear()
  // Pages keep the open page in the address; the next test starts on the start page again.
  window.history.replaceState(null, '', '/')
})

// Components under test never open a real realtime connection.
vi.mock('../realtime/projectEvents', () => ({
  subscribeToProject: vi.fn(() => () => {}),
  useProjectEvents: vi.fn(),
}))

vi.mock('../realtime/notificationEvents', () => ({
  subscribeToNotifications: vi.fn(() => () => {}),
  useNotificationEvents: vi.fn(),
}))
