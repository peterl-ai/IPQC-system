import type { Permission, Role } from '@/types/auth'
import type { CurrentUser } from './authSession'

export const rolePermissions: Readonly<Record<Role, readonly Permission[]>> = {
  admin: ['home:view', 'standards:view', 'plans:view', 'tasks:view', 'records:view', 'users:view', 'provisioning:view'],
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
  'provisioning:view': 'DeviceRegistry.Read',
  'pic-roster:view': 'PicRoster.Read',
}

export function hasPermission(granted: readonly string[] | Role, permission?: Permission): boolean {
  if (permission === undefined) return true
  if (typeof granted === 'string') return rolePermissions[granted].includes(permission)
  return granted.includes(serverPermission[permission])
}

export function canReadPicRoster(user: CurrentUser | null): boolean {
  return !!user && user.role === 'Admin' && user.isActive && !user.mustChangePassword &&
    hasPermission(user.permissions, 'pic-roster:view')
}

export function canWritePicRoster(user: CurrentUser | null): boolean {
  return canReadPicRoster(user) && !!user?.permissions.includes('PicRoster.Write')
}

export function defaultRouteForRole(role: Role): string {
  return role === 'ipqa' ? '/records' : '/home'
}
