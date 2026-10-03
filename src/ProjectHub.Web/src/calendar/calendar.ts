import { apiDownload } from '../api/client'

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

/** File name like the server's: no path or reserved characters, at most 60 characters. */
export function calendarFileName(title: string): string {
  const cleaned = title
    .replace(/[\\/:*?"<>|\p{Cc}]/gu, ' ')
    .split(' ')
    .filter(Boolean)
    .join(' ')
    .slice(0, 60)
    .trim()
  return `${cleaned || 'termin'}.ics`
}

export const downloadTaskCalendar = (taskId: string, title: string) =>
  apiDownload(`/api/v1/tasks/${taskId}/calendar.ics`, calendarFileName(title))

export const downloadMilestoneCalendar = (milestoneId: string, name: string) =>
  apiDownload(`/api/v1/gantt-milestones/${milestoneId}/calendar.ics`, calendarFileName(name))
