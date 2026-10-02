import { describe, expect, it } from 'vitest'
import { defaultRouteForRole, hasPermission, rolePermissions } from './permissions'

describe('role permission mapping', () => {
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
