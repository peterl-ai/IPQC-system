import { describe, expect, it } from 'vitest'
import type { RouteLocationNormalized } from 'vue-router'
import { routeIsAuthorized } from './index'
import { authSession } from '@/services/authSession'

function routeWith(permission: RouteLocationNormalized['meta']['permission']) {
  return { meta: { permission } } as Pick<RouteLocationNormalized, 'meta'>
}

describe('route authorization helper', () => {
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
