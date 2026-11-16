import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { ApiError, patrolStandardsApi } from './api'
import type { PhotoPolicyInput } from './photoPolicyModel'

beforeEach(() => { authSession.initialized.value = true; authSession.accessToken.value = 'synthetic-only' })
afterEach(() => { vi.unstubAllGlobals(); authSession.clear() })
const input: PhotoPolicyInput = { requestId: '00000000-0000-4000-8000-000000000001', canonicalizationVersion: 1,
  expectedDefinitionId: null, expectedDefinitionHash: 'A'.repeat(64), expectedConfirmationId: null, value: 'Required' }
describe('Existing photo-policy HTTP protocol', () => {
  it('sends only the six approved fields, original nulls, bearer and no-store on an exact retry', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{}', { status: 200 })); vi.stubGlobal('fetch', fetch)
    await patrolStandardsApi.confirmPhotoPolicy('standard', 'item', input)
    fetch.mockResolvedValue(new Response('{}', { status: 200 }))
    await patrolStandardsApi.confirmPhotoPolicy('standard', 'item', { ...input })
    for (const [path, init] of fetch.mock.calls) {
      expect(path).toBe('/api/patrol-standards/standard/items/item/photo-policy-confirmations')
      expect(init.method).toBe('POST'); expect(init.cache).toBe('no-store'); expect(init.credentials).toBe('same-origin')
      expect(init.headers.get('Authorization')).toBe('Bearer synthetic-only'); expect(JSON.parse(init.body)).toEqual(input)
      expect(Object.keys(JSON.parse(init.body)).sort()).toEqual(Object.keys(input).sort())
    }
    expect(fetch.mock.calls[0]![1].body).toBe(fetch.mock.calls[1]![1].body)
  })
  it('reads authoritative policy and immutable receipt through existing same-origin no-store routes', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ standardId: 's', items: [], reviewStandard: null })))
      .mockResolvedValueOnce(new Response('{}')); vi.stubGlobal('fetch', fetch)
    await patrolStandardsApi.photoPolicies('s'); await patrolStandardsApi.photoPolicyReceipt('receipt')
    expect(fetch.mock.calls.map(x => x[0])).toEqual(['/api/patrol-standards/s/photo-policies', '/api/photo-policy-confirmations/receipt'])
    expect(fetch.mock.calls.every(x => x[1].cache === 'no-store')).toBe(true)
  })
  it.each([400, 403, 404, 409, 429, 503])('propagates HTTP%s without fake acceptance or automatic write replay', async status => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'synthetic' }), { status })); vi.stubGlobal('fetch', fetch)
    await expect(patrolStandardsApi.confirmPhotoPolicy('s', 'i', input)).rejects.toMatchObject({ status })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
  it('uses the existing one-refresh 401 protocol and preserves the exact payload on its authenticated retry', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ accessToken: 'synthetic-refreshed' })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'synthetic-user', role: 'PQE' })))
      .mockResolvedValueOnce(new Response('{}')); vi.stubGlobal('fetch', fetch)
    await patrolStandardsApi.confirmPhotoPolicy('s', 'i', input)
    const posts = fetch.mock.calls.filter(x => String(x[0]).endsWith('photo-policy-confirmations'))
    expect(posts).toHaveLength(2); expect(posts[0]![1].body).toBe(posts[1]![1].body)
  })
  it('reports network unknown instead of creating an approval locally', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(patrolStandardsApi.confirmPhotoPolicy('s', 'i', input)).rejects.toBeInstanceOf(ApiError)
  })
})
