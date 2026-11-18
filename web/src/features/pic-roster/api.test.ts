import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { picRosterApi } from './api'
import { normalizePicText, picTime, type Pic, type PicAction, type PicIntent } from './model'

const actor = '10000000-0000-0000-0000-000000000001'
const picId = '20000000-0000-0000-0000-000000000001'
const requestId = '30000000-0000-0000-0000-000000000001'
const base = '/api/admin/pic-roster'
const pic: Pic = { picId, revision: 7, name: 'Lee', department: 'Quality', isActive: true }
const send = vi.fn<typeof fetch>()
const page = (items: unknown[], n = 1, hasMore = false) => Response.json({ items, page: n, pageSize: 20, hasMore })
const operation = (action: PicAction): PicIntent => ({
  actorId: actor, requestId, action, picId: action === 'Created' ? null : picId,
  expectedRevision: action === 'Created' ? 0 : 7,
  values: { name: pic.name, department: pic.department, isActive: action !== 'Deactivated' },
})
const receipt = (intent: PicIntent) => ({
  ...pic, ...intent.values, revision: intent.expectedRevision + 1, action: intent.action, requestId,
  actorUserId: actor, actorPersonalSessionId: picId, actorUsername: 'admin-test',
  contractVersion: 1, environmentId: 'testing', occurredAtUtc: '2026-07-01T20:00:00Z',
})

beforeEach(() => {
  send.mockReset()
  vi.stubGlobal('fetch', send)
  authSession.initialized.value = true
  authSession.user.value = {
    id: actor, username: 'admin-test', displayName: 'Admin', role: 'Admin',
    isActive: true, mustChangePassword: false, permissions: ['PicRoster.Read', 'PicRoster.Write'],
  }
  authSession.accessToken.value = 'test-access'
})
afterEach(() => {
  authSession.clear()
  vi.unstubAllGlobals()
})

describe('PIC roster HTTP contract', () => {
  it('filters on the server and reads hasMore without a total, retaining duplicate names', async () => {
    const sameName = { ...pic, picId: actor }
    send.mockResolvedValueOnce(page([pic, sameName], 1, true)).mockResolvedValueOnce(page([], 2)).mockResolvedValueOnce(page([]))
    expect(await picRosterApi.list(actor, 1, undefined)).toEqual({ page: 1, pageSize: 20, items: [pic, sameName], hasMore: true })
    expect((await picRosterApi.list(actor, 2, true)).hasMore).toBe(false)
    await picRosterApi.list(actor, 1, false)
    expect(send.mock.calls.map(call => call[0])).toEqual([
      base + '?page=1&pageSize=20', base + '?page=2&pageSize=20&active=true', base + '?page=1&pageSize=20&active=false',
    ])
  })

  it('reads latest and paginated immutable actor/value snapshots independently', async () => {
    const original = { ...receipt(operation('Created')), name: 'Historical', isActive: false }
    send.mockResolvedValueOnce(Response.json(pic)).mockResolvedValueOnce(page([original], 2))
    expect(await picRosterApi.current(actor, picId)).toEqual(pic)
    expect((await picRosterApi.history(actor, picId, 2)).items).toEqual([original])
    expect(send.mock.calls.map(call => call[0])).toEqual([base + '/' + picId, base + '/' + picId + '/history?page=2&pageSize=20'])
    expect(picTime(original.occurredAtUtc, 'en')).toMatch(/4:00:00 PM EDT/)
    expect(picTime('2026-01-01T20:00:00Z', 'en')).toMatch(/3:00:00 PM EST/)
  })

  it.each([
    ['Created', 'POST', '', { name: pic.name, department: pic.department, isActive: true }],
    ['Updated', 'PUT', '/' + picId, { expectedRevision: 7, name: pic.name, department: pic.department }],
    ['Activated', 'POST', '/' + picId + '/activate', { expectedRevision: 7 }],
    ['Deactivated', 'POST', '/' + picId + '/deactivate', { expectedRevision: 7 }],
  ] as const)('serializes the exact %s request', async (action, method, path, fields) => {
    const intent = operation(action)
    send.mockResolvedValueOnce(Response.json({ receipt: receipt(intent), replayOnly: false }, { status: action === 'Created' ? 201 : 200 }))
    expect(await picRosterApi.mutate(intent)).toEqual(receipt(intent))
    expect(send.mock.calls[0]?.[0]).toBe(base + path)
    expect(send.mock.calls[0]?.[1]).toMatchObject({ method, body: JSON.stringify({ requestId, ...fields }), credentials: 'same-origin' })
    expect(new Headers(send.mock.calls[0]?.[1]?.headers).get('Authorization')).toBe('Bearer test-access')
  })

  it('recovers a replayed create on 200 and verifies the receipt lookup', async () => {
    const intent = operation('Created')
    const original = receipt(intent)
    send.mockResolvedValueOnce(Response.json({ receipt: original, replayOnly: true })).mockResolvedValueOnce(Response.json(original))
    expect(await picRosterApi.mutate(intent)).toEqual(original)
    expect(await picRosterApi.receipt(intent)).toEqual(original)
    expect(send.mock.calls[1]?.[0]).toBe(base + '/requests/' + requestId + '/receipt')
  })

  it.each(['requestId', 'picId', 'actorUserId', 'action', 'revision', 'name', 'department', 'isActive'] as const)(
    'rejects an otherwise well-shaped mutation result with a mismatched %s', async key => {
      const intent = operation('Updated')
      const replacements = {
        requestId: actor, picId: actor, actorUserId: picId, action: 'Activated',
        revision: 18, name: 'Another', department: 'Other', isActive: false,
      }
      send.mockResolvedValueOnce(Response.json({ receipt: { ...receipt(intent), [key]: replacements[key] }, replayOnly: false }))
      await expect(picRosterApi.mutate(intent)).rejects.toThrow('Unexpected PIC mutation result')
    },
  )

  it('rejects raw records and ambiguous success status or replay flags', async () => {
    const intent = operation('Created')
    send.mockResolvedValueOnce(Response.json(pic, { status: 201 }))
      .mockResolvedValueOnce(Response.json({ receipt: receipt(intent), replayOnly: false }))
      .mockResolvedValueOnce(Response.json({ receipt: receipt(intent), replayOnly: 'yes' }, { status: 201 }))
    for (let i = 0; i < 3; i++) await expect(picRosterApi.mutate(intent)).rejects.toThrow('Unexpected PIC mutation result')
  })

  it('does not send after the actor changes at the async auth boundary', async () => {
    const task = picRosterApi.mutate(operation('Created'))
    authSession.user.value = { ...authSession.user.value!, id: picId }
    authSession.accessToken.value = 'other-test-access'
    await expect(task).rejects.toMatchObject({ status: 403 })
    expect(send).not.toHaveBeenCalled()
  })

  it('blocks view-only mutations and never automatically resends a mutation due to 401', async () => {
    const intent = operation('Created')
    authSession.user.value = { ...authSession.user.value!, permissions: ['PicRoster.Read'] }
    await expect(picRosterApi.mutate(intent)).rejects.toMatchObject({ status: 403 })
    expect(send).not.toHaveBeenCalled()
    authSession.user.value.permissions.push('PicRoster.Write')
    send.mockResolvedValueOnce(new Response(null, { status: 401 }))
    await expect(picRosterApi.mutate(intent)).rejects.toMatchObject({ status: 401 })
    expect(send).toHaveBeenCalledTimes(1)
  })

  it('does not replay a guarded read if refresh signs in a different user', async () => {
    const another = { ...authSession.user.value!, id: picId }
    send.mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(Response.json({ accessToken: 'other-test-access' }))
      .mockResolvedValueOnce(Response.json(another))
    await expect(picRosterApi.list(actor, 1, undefined)).rejects.toMatchObject({ status: 403 })
    expect(send.mock.calls.map(call => call[0])).toEqual([base + '?page=1&pageSize=20', '/api/auth/refresh', '/api/me'])
    expect(authSession.user.value?.id).toBe(picId)
  })
})

describe('PIC text validation', () => {
  it('matches the raw UTF-16, Unicode scalar and trimming boundaries', () => {
    expect(normalizePicText('　Alpha  ')).toBe('Alpha')
    expect(normalizePicText('😀'.repeat(200))).toBe('😀'.repeat(200))
    expect(normalizePicText('a'.repeat(201))).toBeNull()
    expect(normalizePicText(' '.repeat(400) + 'a')).toBeNull()
    expect(normalizePicText('a\n')).toBeNull()
    expect(normalizePicText('a\u0085')).toBeNull()
    expect(normalizePicText('\ud800')).toBeNull()
    expect(normalizePicText('   ')).toBeNull()
    expect(normalizePicText('\ufeff')).toBe('\ufeff')
  })
})
