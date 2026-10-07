import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  localStorage.clear()
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
