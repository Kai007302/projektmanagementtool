/**
 * Reads a date the way people type it (Postel's law): "30.11.", "30.11.26", "30.11.2026", "2026-11-30", "heute",
 * "morgen", "übermorgen", "Fr" or "Freitag" (the next one), "+3" (in three days) or "+2w" (in two weeks).
 * Returns the calendar day as YYYY-MM-DD, '' for empty text and null when the text is not a date.
 */
export function parseDate(text: string, now = new Date()): string | null {
  const value = text.trim().toLowerCase().replace(/\s+/g, ' ')
  if (value === '') return ''
  const base = new Date(now.getFullYear(), now.getMonth(), now.getDate())

  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(value)
  if (iso) return checked(Number(iso[1]), Number(iso[2]), Number(iso[3]))

  const german = /^(\d{1,2})\.(\d{1,2})\.?(\d{2}|\d{4})?$/.exec(value)
  if (german) {
    const day = Number(german[1])
    const month = Number(german[2])
    if (german[3]) return checked(german[3].length === 2 ? 2000 + Number(german[3]) : Number(german[3]), month, day)
    // Without a year: the next such day from today on.
    const thisYear = checked(base.getFullYear(), month, day)
    return thisYear !== null && thisYear < format(base) ? checked(base.getFullYear() + 1, month, day) : thisYear
  }

  const words: Record<string, number> = { heute: 0, morgen: 1, übermorgen: 2, uebermorgen: 2, gestern: -1 }
  if (value in words) return format(addDays(base, words[value]))

  const relative = /^([+-])\s?(\d{1,3})\s?(t|tag|tage|w|wo|woche|wochen)?$/.exec(value)
  if (relative) {
    const amount = Number(relative[2]) * (relative[3]?.startsWith('w') ? 7 : 1)
    return format(addDays(base, relative[1] === '-' ? -amount : amount))
  }

  const weekday = weekdays.findIndex((names) => names.includes(value.replace(/\.$/, '')))
  if (weekday >= 0) {
    const ahead = (weekday - base.getDay() + 7) % 7 || 7
    return format(addDays(base, ahead))
  }

  return null
}

/** A calendar day (YYYY-MM-DD) as people read it: "30.11.2026". */
export function formatDate(iso: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  return match ? `${match[3]}.${match[2]}.${match[1]}` : iso
}

// Sunday first, like Date.getDay().
const weekdays = [
  ['so', 'sonntag'],
  ['mo', 'montag'],
  ['di', 'dienstag'],
  ['mi', 'mittwoch'],
  ['do', 'donnerstag'],
  ['fr', 'freitag'],
  ['sa', 'samstag'],
]

function addDays(date: Date, days: number) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
}

function format(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** The day if it exists (no 31.02.), else null. */
function checked(year: number, month: number, day: number) {
  const date = new Date(year, month - 1, day)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? format(date) : null
}
