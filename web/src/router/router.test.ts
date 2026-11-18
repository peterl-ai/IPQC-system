import { describe, expect, it } from 'vitest'
import type { RouteLocationNormalized } from 'vue-router'
import { router, routeIsAuthorized } from './index'
import { authSession } from '@/services/authSession'

function routeWith(permission: RouteLocationNormalized['meta']['permission']) {
  return { meta: { permission } } as Pick<RouteLocationNormalized, 'meta'>
}

describe('route authorization helper', () => {
  it('guards direct PIC roster navigation for Admin, PQE and IPQA', async () => {
    const admin = { id: '1', username: 'admin', displayName: 'Admin', role: 'Admin' as const, isActive: true, mustChangePassword: false, permissions: ['PicRoster.Read'] }
    authSession.initialized.value = true
    authSession.user.value = admin
    await router.push('/pic-roster')
    expect(router.currentRoute.value.name).toBe('pic-roster')
    for (const user of [{ ...admin, permissions: [] }, { ...admin, role: 'PQE' as const }, { ...admin, role: 'IPQA' as const }]) {
      authSession.user.value = user
      await router.push('/forbidden')
      await router.push('/pic-roster')
      expect(router.currentRoute.value.name).toBe('forbidden')
      expect(router.currentRoute.value.query.from).toBe('/pic-roster')
    }
  })

  it('requires the provisioning permission even when navigating directly', () => {
    authSession.user.value = { id: '1', username: 'pqe', displayName: 'PQE', role: 'PQE', isActive: true, mustChangePassword: false, permissions: ['PatrolTasks.Review'] }
    expect(routeIsAuthorized(routeWith('provisioning:view'))).toBe(false)
    authSession.user.value = { ...authSession.user.value, role: 'Admin', permissions: ['DeviceRegistry.Read'] }
    expect(routeIsAuthorized(routeWith('provisioning:view'))).toBe(true)
  })
  it('allows an authorized role', () => {
    authSession.user.value = { id: '1', username: 'pqe', displayName: 'PQE', role: 'PQE', isActive: true, mustChangePassword: false, permissions: ['PatrolTasks.Review'] }
    expect(routeIsAuthorized(routeWith('tasks:view'))).toBe(true)
  })

  it('blocks a direct route access for an unauthorized role', () => {
    authSession.user.value = { id: '2', username: 'ipqa', displayName: 'IPQA', role: 'IPQA', isActive: true, mustChangePassword: false, permissions: ['CompletedRecords.Read', 'PatrolTasks.ExecuteOwn'] }
    expect(routeIsAuthorized(routeWith('home:view'))).toBe(false)
    expect(routeIsAuthorized(routeWith('standards:view'))).toBe(false)
    expect(routeIsAuthorized(routeWith('plans:view'))).toBe(false)
    expect(routeIsAuthorized(routeWith('tasks:view'))).toBe(false)
    expect(routeIsAuthorized(routeWith('users:view'))).toBe(false)
  })

  it('allows IPQA to access Completed Patrol Records', () => {
    authSession.user.value = { id: '2', username: 'ipqa', displayName: 'IPQA', role: 'IPQA', isActive: true, mustChangePassword: false, permissions: ['CompletedRecords.Read'] }
    expect(routeIsAuthorized(routeWith('records:view'))).toBe(true)
  })

  it('allows public status routes without a permission', () => {
    expect(routeIsAuthorized(routeWith(undefined))).toBe(true)
  })
})
