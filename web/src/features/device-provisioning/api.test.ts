import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { errorCode, ProvisioningApiError, provisioningApi, type Device, type Line } from './api'
import { enrollmentApi } from './enrollmentApi'
const line: Line = { id: 'line', code: 'L', name: 'Line', revision: 3, isActive: true }
const device = { id: 'device', revision: 4 } as Device
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status })
beforeEach(() => { authSession.initialized.value = true; authSession.accessToken.value = 'test-token' })
afterEach(() => vi.unstubAllGlobals())
describe('Retained provisioning API client', () => {
  it('uses current personal credentials and exact revisions on retained Admin routes', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json({}))); vi.stubGlobal('fetch', fetch)
    await provisioningApi.createLine('L', 'Line'); await provisioningApi.renameLine(line, 'New'); await provisioningApi.setLineActive(line, false)
    await provisioningApi.revokeDevice(device, 'Retire'); await provisioningApi.audit('devices', device.id)
    await enrollmentApi.list(device.id); await enrollmentApi.cancel('old-request', 8, 'Close')
    expect(fetch.mock.calls.map(c => c[0])).toEqual(['/api/admin/production-lines', '/api/admin/production-lines/line', '/api/admin/production-lines/line/deactivate', '/api/admin/devices/device/revoke', '/api/admin/devices/device/audit?page=1&pageSize=20', '/api/admin/devices/device/enrollments?page=1&pageSize=20', '/api/admin/enrollments/old-request/cancel'])
    expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual({ expectedRevision: 4, reason: 'Retire' })
    expect(JSON.parse(fetch.mock.calls[6][1].body)).toEqual({ expectedRevision: 8, reason: 'Close' })
    expect((fetch.mock.calls[0][1].headers as Headers).get('Authorization')).toBe('Bearer test-token')
    expect(Object.keys(provisioningApi)).not.toEqual(expect.arrayContaining(['createDevice', 'editDevice', 'deviceAction']))
    expect(Object.keys(enrollmentApi)).toEqual(['list', 'cancel'])
  })
  it('loads all line-name lookup pages for historical audit display', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({ items: [line], total: 2 })).mockResolvedValueOnce(json({ items: [{ ...line, id: 'second' }], total: 2 }))
    vi.stubGlobal('fetch', fetch)
    expect((await provisioningApi.lineOptions()).map(x => x.id)).toEqual(['line', 'second'])
    expect(fetch.mock.calls[1][0]).toContain('page=2&pageSize=100')
  })
  it('maps retirement, conflict and authorization errors to localized safe codes', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ code: 'Conflict' }, 409)))
    await expect(provisioningApi.revokeDevice(device, 'Retire')).rejects.toMatchObject({ code: 'Conflict', status: 409 })
    expect(errorCode(new ProvisioningApiError('DeviceProvisioningRetired', 410))).toBe('DeviceProvisioningRetired')
    expect(errorCode(new ProvisioningApiError('internal-detail', 400))).toBe('InvalidInput')
    expect(errorCode(new ProvisioningApiError('', 403))).toBe('Forbidden')
    expect(errorCode(new Error('offline'))).toBe('Unavailable')
  })
})
