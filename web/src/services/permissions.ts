import type { Permission, Role } from '@/types/auth'

export const rolePermissions: Readonly<Record<Role, readonly Permission[]>> = {
  admin: ['home:view', 'standards:view', 'plans:view', 'tasks:view', 'records:view', 'users:view'],
  pqe: ['home:view', 'standards:view', 'plans:view', 'tasks:view', 'records:view'],
  ipqa: ['home:view', 'records:view'],
}

export function hasPermission(role: Role, permission?: Permission): boolean {
  return permission === undefined || rolePermissions[role].includes(permission)
}

export function defaultRouteForRole(role: Role): string {
  return role === 'ipqa' ? '/records' : '/home'
}
