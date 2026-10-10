import { beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { usersApi } from './api'

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
describe('persistent user management API', () => {
  beforeEach(() => { authSession.initialized.value = true; authSession.accessToken.value = 'test-token'; vi.unstubAllGlobals() })

  it('lists with filters and pagination, then creates and edits users', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({ items: [], total: 0, page: 2, pageSize: 5 }))
      .mockResolvedValueOnce(json({ id: 'user-1' }, 201)).mockResolvedValueOnce(json({ id: 'user-1' }))
    vi.stubGlobal('fetch', fetch)
    await usersApi.list({ username: 'ipqa', displayName: 'Lee', role: 'IPQA', active: true, page: 2, pageSize: 5 })
    expect(Object.fromEntries(new URL(fetch.mock.calls[0][0], 'http://localhost').searchParams))
      .toMatchObject({ username: 'ipqa', displayName: 'Lee', role: 'IPQA', active: 'true', page: '2', pageSize: '5' })
    expect((fetch.mock.calls[0][1].headers as Headers).get('Authorization')).toBe('Bearer test-token')
    await usersApi.create({ username: 'ipqa03', displayName: 'IPQA 03', role: 'IPQA', initialPassword: 'InitialPass123', isActive: true })
    expect(JSON.parse(fetch.mock.calls[1][1].body).username).toBe('ipqa03')
    await usersApi.update('user-1', 'New Name', 'IPQA')
    expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ displayName: 'New Name', role: 'IPQA' })
  })

  it('uses explicit activate/deactivate/reset endpoints and surfaces conflicts', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({})).mockResolvedValueOnce(json({}))
      .mockResolvedValueOnce(json({})).mockResolvedValueOnce(json({ title: 'The last active Admin must remain active.' }, 409))
    vi.stubGlobal('fetch', fetch)
    await usersApi.setActive('user-1', false)
    await usersApi.setActive('user-1', true)
    await usersApi.resetPassword('user-1', 'TemporaryPass123')
    await expect(usersApi.setActive('user-1', false)).rejects.toMatchObject({ status: 409 })
    expect(fetch.mock.calls.map(call => call[0])).toEqual([
      '/api/users/user-1/deactivate', '/api/users/user-1/activate',
      '/api/users/user-1/reset-password', '/api/users/user-1/deactivate',
    ])
  })
})
