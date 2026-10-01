import { describe, expect, it } from 'vitest'
import type { RouteLocationNormalized } from 'vue-router'
import { routeIsAuthorized } from './index'

function routeWith(permission: RouteLocationNormalized['meta']['permission']) {
  return { meta: { permission } } as Pick<RouteLocationNormalized, 'meta'>
}

describe('route authorization helper', () => {
  it('allows an authorized role', () => {
    expect(routeIsAuthorized(routeWith('tasks:view'), 'pqe')).toBe(true)
  })

  it('blocks a direct route access for an unauthorized role', () => {
    expect(routeIsAuthorized(routeWith('tasks:view'), 'ipqa')).toBe(false)
    expect(routeIsAuthorized(routeWith('users:view'), 'pqe')).toBe(false)
  })

  it('allows public status routes without a permission', () => {
    expect(routeIsAuthorized(routeWith(undefined), 'ipqa')).toBe(true)
  })
})
