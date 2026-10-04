const format = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'short' })
const formatLong = new Intl.DateTimeFormat('de-DE', { dateStyle: 'full' })

function today() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

/** Due date as a chip ("14. Okt."); overdue open tasks are marked red. Dates are calendar days (YYYY-MM-DD). */
export function DueDate({ date, done }: { date: string; done: boolean }) {
  const [year, month, day] = date.split('-').map(Number)
  const value = new Date(year, month - 1, day)
  const overdue = !done && date < today()
  return (
    <span className={overdue ? 'task-chip due overdue' : 'task-chip due'} title={`Fällig: ${formatLong.format(value)}`}>
      <span aria-hidden="true">{overdue ? '⏰' : '📅'}</span> {overdue && 'Überfällig · '}
      {format.format(value)}
    </span>
  )
}
