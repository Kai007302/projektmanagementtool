import { useEffect, useState } from 'react'
import type { Me } from '../identity/api'
import { fetchDepartments } from './api'

type Option = { id: string; name: string }

/**
 * Chooses the department whose projects and knowledge are shown (ADR 0021). People see their own departments,
 * organization admins all of them. Hidden for people in no department.
 */
export function DepartmentSwitcher({ me, value, onChange }: { me: Me; value: string; onChange: (departmentId: string) => void }) {
  const [all, setAll] = useState<Option[] | null>(null)
  const admin = me.organizationRole === 'admin'

  useEffect(() => {
    if (!admin) return
    fetchDepartments().then((page) => setAll(page.items), () => setAll(null))
  }, [admin])

  const options: Option[] = admin && all ? all : me.departments
  if (options.length === 0) return null

  return (
    <select className="department-switcher" aria-label="Abteilung" value={value} onChange={(event) => onChange(event.target.value)}>
      <option value="">Alle Abteilungen</option>
      {options.map((department) => (
        <option key={department.id} value={department.id}>
          {department.name}
        </option>
      ))}
      {value && !options.some((d) => d.id === value) && <option value={value}>Gewählte Abteilung</option>}
    </select>
  )
}
