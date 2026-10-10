import type { Permission, Role } from '@/types/auth'

export const rolePermissions: Readonly<Record<Role, readonly Permission[]>> = {
  admin: ['home:view', 'standards:view', 'plans:view', 'tasks:view', 'records:view', 'users:view'],
  pqe: ['home:view', 'standards:view', 'plans:view', 'tasks:view', 'records:view'],
  ipqa: ['records:view'],
}

const serverPermission: Record<Permission, string> = {
  'home:view': 'PatrolStandards.Read',
  'standards:view': 'PatrolStandards.Read',
  'plans:view': 'PatrolPlans.Read',
  'tasks:view': 'PatrolTasks.Review',
  'records:view': 'CompletedRecords.Read',
  'users:view': 'Users.Read',
}

export function hasPermission(granted: readonly string[] | Role, permission?: Permission): boolean {
  if (permission === undefined) return true
  if (typeof granted === 'string') return rolePermissions[granted].includes(permission)
  return granted.includes(serverPermission[permission])
}

export function defaultRouteForRole(role: Role): string {
  return role === 'ipqa' ? '/records' : '/home'
}
