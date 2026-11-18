import { describe, expect, it } from 'vitest'
import { canReadPicRoster, canWritePicRoster, defaultRouteForRole, hasPermission, rolePermissions } from './permissions'

describe('role permission mapping', () => {
  it('requires granular PIC permissions in addition to current Admin authority', () => {
    const admin = { id: '1', username: 'test', displayName: 'Admin', role: 'Admin' as const, isActive: true, mustChangePassword: false, permissions: ['PicRoster.Read'] }
    expect(hasPermission('admin', 'pic-roster:view')).toBe(false)
    expect(hasPermission(admin.permissions, 'pic-roster:view')).toBe(true)
    expect(canReadPicRoster(admin)).toBe(true)
    expect(canWritePicRoster(admin)).toBe(false)
    expect(canWritePicRoster({ ...admin, permissions: ['PicRoster.Read', 'PicRoster.Write'] })).toBe(true)
    expect(canWritePicRoster({ ...admin, permissions: ['PicRoster.Write'] })).toBe(false)
    expect(canReadPicRoster({ ...admin, role: 'PQE' })).toBe(false)
    expect(canReadPicRoster({ ...admin, isActive: false })).toBe(false)
    expect(canReadPicRoster({ ...admin, mustChangePassword: true })).toBe(false)
  })

  it('keeps provisioning outside PQE and IPQA business permissions', () => {
    expect(hasPermission('admin', 'provisioning:view')).toBe(true)
    expect(hasPermission('pqe', 'provisioning:view')).toBe(false)
    expect(hasPermission('ipqa', 'provisioning:view')).toBe(false)
    expect(hasPermission(['DeviceRegistry.Read'], 'provisioning:view')).toBe(true)
    expect(hasPermission(['PatrolStandards.Read', 'Users.Read'], 'provisioning:view')).toBe(false)
  })
  it('grants Admin every Phase 1A web permission', () => {
    expect(rolePermissions.admin).toEqual(expect.arrayContaining([
      'standards:view', 'plans:view', 'tasks:view', 'records:view', 'users:view',
    ]))
  })

  it('prevents PQE from accessing User Management', () => {
    expect(hasPermission('pqe', 'records:view')).toBe(true)
    expect(hasPermission('pqe', 'users:view')).toBe(false)
  })

  it('limits IPQA web access to Completed Patrol Records only', () => {
    expect(rolePermissions.ipqa).toEqual(['records:view'])
    expect(hasPermission('ipqa', 'home:view')).toBe(false)
    expect(hasPermission('ipqa', 'standards:view')).toBe(false)
    expect(hasPermission('ipqa', 'plans:view')).toBe(false)
    expect(hasPermission('ipqa', 'tasks:view')).toBe(false)
    expect(hasPermission('ipqa', 'users:view')).toBe(false)
    expect(hasPermission('ipqa', 'records:view')).toBe(true)
    expect(defaultRouteForRole('ipqa')).toBe('/records')
  })
})
