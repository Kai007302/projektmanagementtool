import { apiDownload, apiFetch, safeFileName } from '../api/client'

/** The day after an ISO date (yyyy-mm-dd), because all-day entries end exclusively. */
export function nextDay(day: string): string {
  const date = new Date(`${day}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 1)
  return date.toISOString().slice(0, 10)
}

/**
 * Opens the new-event form of Outlook on the web with title and dates filled in (DEC-028). Runs in the
 * person's own browser and mailbox, so ProjectHub needs no calendar permission in Microsoft Graph.
 */
export function outlookComposeUrl(title: string, firstDay: string, lastDay: string, appUrl: string): string {
  const end = nextDay(lastDay < firstDay ? firstDay : lastDay)
  const query = new URLSearchParams({
    path: '/calendar/action/compose',
    rru: 'addevent',
    subject: title,
    startdt: firstDay,
    enddt: end,
    allday: 'true',
    body: `In ProjectHub öffnen: ${appUrl}`,
  })
  return `https://outlook.office.com/calendar/0/deeplink/compose?${query.toString()}`
}

export const calendarFileName = (title: string) => `${safeFileName(title, 'termin')}.ics`

export const downloadTaskCalendar = (taskId: string, title: string) =>
  apiDownload(`/api/v1/tasks/${taskId}/calendar.ics`, calendarFileName(title))

export const downloadMilestoneCalendar = (milestoneId: string, name: string) =>
  apiDownload(`/api/v1/gantt-milestones/${milestoneId}/calendar.ics`, calendarFileName(name))

export type CalendarFeedStatus = { active: boolean; createdAt: string | null; lastUsedAt: string | null }

export type CalendarFeedCreated = { url: string; createdAt: string }

export const fetchCalendarFeed = () => apiFetch<CalendarFeedStatus>('/api/v1/me/calendar-feed')

/** Creates the feed address or replaces it; the old address stops working. The address is only shown now. */
export const createCalendarFeed = () => apiFetch<CalendarFeedCreated>('/api/v1/me/calendar-feed', { method: 'POST' })

export const deleteCalendarFeed = () => apiFetch<void>('/api/v1/me/calendar-feed', { method: 'DELETE' })

/** The same address with the webcal scheme, which opens the subscription dialog of the calendar program. */
export const webcalUrl = (url: string) => url.replace(/^https?:/, 'webcal:')
