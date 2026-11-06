import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { errorCode, ProvisioningApiError, provisioningApi, type Device, type Line } from './api'
const line: Line = { id: 'line', code: 'L', name: 'Line', revision: 3, isActive: true }
const device = { id: 'device', revision: 4 } as Device
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status })
beforeEach(() => { authSession.initialized.value = true; authSession.accessToken.value = 'test-token' })
afterEach(() => vi.unstubAllGlobals())
describe('provisioning API', () => {
  it('uses same-origin Admin routes, current revisions and explicit lifecycle actions', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json({}))); vi.stubGlobal('fetch', fetch)
    await provisioningApi.createLine('L', 'Line'); await provisioningApi.renameLine(line, 'New'); await provisioningApi.setLineActive(line, false)
    await provisioningApi.createDevice('PAD', 'SM-T500', 'description'); await provisioningApi.editDevice(device, 'New', '', '')
    for (const action of ['bind', 'rebind', 'unbind', 'revoke', 'replace'] as const) await provisioningApi.deviceAction(device, action, { reason: 'Reason' })
    await provisioningApi.audit('devices', device.id)
    expect(fetch.mock.calls.map(c => c[0])).toEqual(['/api/admin/production-lines', '/api/admin/production-lines/line', '/api/admin/production-lines/line/deactivate', '/api/admin/devices', '/api/admin/devices/device', ...['bind', 'rebind', 'unbind', 'revoke', 'replace'].map(a => `/api/admin/devices/device/${a}`), '/api/admin/devices/device/audit?page=1&pageSize=20'])
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ name: 'New', expectedRevision: 3 })
    expect(JSON.parse(fetch.mock.calls[5][1].body)).toEqual({ reason: 'Reason', expectedRevision: 4 })
    expect((fetch.mock.calls[0][1].headers as Headers).get('Authorization')).toBe('Bearer test-token')
    expect(fetch.mock.calls.every(c => !String(c[1].body).includes('Trusted'))).toBe(true)
  })
  it('loads every option page, including lines outside the visible management page', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({ items: [line], total: 2 })).mockResolvedValueOnce(json({ items: [{ ...line, id: 'second' }], total: 2 }))
    vi.stubGlobal('fetch', fetch)
    expect((await provisioningApi.lineOptions()).map(x => x.id)).toEqual(['line', 'second'])
    expect(fetch.mock.calls[1][0]).toContain('page=2&pageSize=100')
  })
  it('maps conflicts, unknown response details and validation/network errors to translatable codes', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ code: 'LineOccupied' }, 409)))
    await expect(provisioningApi.deviceAction(device, 'bind', { lineId: 'line' })).rejects.toMatchObject({ code: 'LineOccupied', status: 409 })
    expect(errorCode(new ProvisioningApiError('Conflict', 409))).toBe('Conflict')
    expect(errorCode(new ProvisioningApiError('internal-detail', 400))).toBe('InvalidInput')
    expect(errorCode(new ProvisioningApiError('', 403))).toBe('Forbidden')
    expect(errorCode(new Error('offline'))).toBe('Unavailable')
  })
})
