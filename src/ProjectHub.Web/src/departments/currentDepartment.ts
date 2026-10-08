import type { Me } from '../identity/api'

const keyOf = (userId: string) => `projecthub.department.${userId}`

/**
 * The department someone works in right now (ADR 0021): remembered per person in this browser. An empty string means
 * all departments the person can see, which is also where everyone starts, so nobody sees less after the update.
 */
export function readDepartment(me: Me): string {
  let stored: string | null = null
  try {
    stored = localStorage.getItem(keyOf(me.id))
  } catch {
    // Storage unavailable: show all departments.
  }
  // Organization admins may pick any department, others only their own (or all).
  if (stored !== null && (stored === '' || me.organizationRole === 'admin' || me.departments.some((d) => d.id === stored))) return stored
  return ''
}

export function rememberDepartment(me: Me, departmentId: string): void {
  try {
    localStorage.setItem(keyOf(me.id), departmentId)
  } catch {
    // Storage unavailable: the choice lasts until the page reloads.
  }
}

/** Whether the person may manage a department: organization admins and its leads. */
export const canManageDepartment = (me: Me, departmentId: string) =>
  me.organizationRole === 'admin' || me.departments.some((d) => d.id === departmentId && d.role === 'lead')

/** Admins and leads see the "Verwaltung" area. */
export const managesAnything = (me: Me) => me.organizationRole === 'admin' || me.departments.some((d) => d.role === 'lead')
