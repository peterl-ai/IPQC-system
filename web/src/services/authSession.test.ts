import { beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession, type CurrentUser } from './authSession'
import { authFetch } from './authFetch'

const user: CurrentUser = { id: '1', username: 'admin', displayName: 'Admin User', role: 'Admin',
  isActive: true, mustChangePassword: false, permissions: ['Users.Read'] }
const auth = { accessToken: 'new-token', accessExpiresAtUtc: '2026-10-09T20:00:00Z', user }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })

describe('local web session', () => {
  beforeEach(() => { authSession.clear(); vi.unstubAllGlobals() })

  it('logs in with real credentials and accepts /api/me as current-user truth', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(auth)).mockResolvedValueOnce(json(user))
    vi.stubGlobal('fetch', fetch)
    await authSession.login('admin', 'TestOnlyPass123')
    expect(authSession.user.value?.username).toBe('admin')
    expect(authSession.accessToken.value).toBe('new-token')
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ username: 'admin', password: 'TestOnlyPass123', clientType: 'Web' })
    expect(fetch.mock.calls[0][1].credentials).toBe('same-origin')
    expect((fetch.mock.calls[1][1].headers as Record<string, string>).Authorization).toBe('Bearer new-token')
  })

  it('rejects invalid credentials without retaining a token', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Invalid username or password.' }, 401)))
    await expect(authSession.login('admin', 'wrong')).rejects.toThrow()
    expect(authSession.accessToken.value).toBeNull()
  })

  it('restores through the cookie refresh path and retries one 401', async () => {
    authSession.initialized.value = false
    const fetch = vi.fn().mockResolvedValueOnce(json(auth)).mockResolvedValueOnce(json(user))
      .mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json({ ...auth, accessToken: 'rotated' }))
      .mockResolvedValueOnce(json(user)).mockResolvedValueOnce(json({ items: [] }))
    vi.stubGlobal('fetch', fetch)
    await authSession.restore()
    expect(authSession.authenticated.value).toBe(true)
    expect((await authFetch('/api/users')).status).toBe(200)
    expect(fetch.mock.calls.filter((call) => call[0] === '/api/auth/refresh')).toHaveLength(2)
    expect((fetch.mock.calls[5][1].headers as Headers).get('Authorization')).toBe('Bearer rotated')
  })

  it('clears a failed refresh and logs out', async () => {
    authSession.accessToken.value = 'expired'
    authSession.user.value = user
    const fetch = vi.fn().mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json({}, 401))
    vi.stubGlobal('fetch', fetch)
    expect((await authFetch('/api/users')).status).toBe(401)
    expect(authSession.user.value).toBeNull()
    authSession.accessToken.value = 'current'
    authSession.user.value = user
    fetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    await authSession.logout()
    expect(authSession.accessToken.value).toBeNull()
    expect(authSession.user.value).toBeNull()
  })

  it('refreshes an expired access token before revoking the logout cookie', async () => {
    authSession.accessToken.value = 'expired'
    authSession.user.value = user
    const fetch = vi.fn().mockResolvedValueOnce(json({}, 401))
      .mockResolvedValueOnce(json(auth)).mockResolvedValueOnce(json(user))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    await authSession.logout()
    expect(fetch.mock.calls.map(call => call[0])).toEqual([
      '/api/auth/logout', '/api/auth/refresh', '/api/me', '/api/auth/logout',
    ])
    expect((fetch.mock.calls[3][1].headers as Record<string, string>).Authorization).toBe('Bearer new-token')
    expect(authSession.user.value).toBeNull()
  })
})
