import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockSession } from '@/services/mockSession'
import { PlanApiError, patrolPlansApi, type PlanDraft } from './api'

const draft: PlanDraft = { planNo: '', planName: 'Plan', patrolStandardId: 'standard-id', factoryCode: '', factoryName: '', lineCode: '', lineName: '', isEnabled: true, effectiveStartLocal: '2026-10-03T08:00', effectiveEndLocal: null, timeZoneId: 'America/New_York', schedule: { type: 'everyNHours', intervalHours: 2, times: null, daysOfWeek: null }, assigneeKey: 'dev-ipqa-1' }
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status })
afterEach(() => vi.unstubAllGlobals())

describe('Patrol Plans API client', () => {
  it('loads filtered pages through the API with development role', async () => {
    mockSession.setRole('pqe')
    const fetch = vi.fn().mockResolvedValue(json({ items: [], total: 0, page: 2, pageSize: 5 }))
    vi.stubGlobal('fetch', fetch)
    await patrolPlansApi.list({ planName: 'Line A', patrolStandardId: 'standard-id', enabled: false, page: 2, pageSize: 5 })
    const query = new URL(fetch.mock.calls[0][0], 'http://localhost').searchParams
    expect(Object.fromEntries(query)).toMatchObject({ planName: 'Line A', patrolStandardId: 'standard-id', enabled: 'false', page: '2', pageSize: '5' })
    expect((fetch.mock.calls[0][1].headers as Headers).get('X-Dev-Role')).toBe('pqe')
  })
  it('uses persistent CRUD and explicit state endpoints', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json({ id: 'plan-id', ...draft })))
    vi.stubGlobal('fetch', fetch)
    await patrolPlansApi.create(draft)
    await patrolPlansApi.get('plan-id')
    await patrolPlansApi.update('plan-id', draft)
    await patrolPlansApi.setEnabled('plan-id', false)
    await patrolPlansApi.remove('plan-id')
    expect(fetch.mock.calls.map((x) => [x[0], x[1].method])).toEqual([
      ['/api/patrol-plans', 'POST'], ['/api/patrol-plans/plan-id', undefined], ['/api/patrol-plans/plan-id', 'PUT'],
      ['/api/patrol-plans/plan-id/disable', 'POST'], ['/api/patrol-plans/plan-id', 'DELETE'],
    ])
    expect(JSON.parse(fetch.mock.calls[0][1].body as string).schedule.intervalHours).toBe(2)
  })
  it('reports validation, conflict, and offline errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ errors: { PlanName: ['Required'] } }, 400)))
    await expect(patrolPlansApi.create(draft)).rejects.toThrow('Required')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Plan has generated tasks.' }, 409)))
    await expect(patrolPlansApi.remove('plan-id')).rejects.toThrow('Plan has generated tasks.')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(patrolPlansApi.get('plan-id')).rejects.toBeInstanceOf(PlanApiError)
  })
  it('loads every persisted standard page for the selector and fixture assignees', async () => {
    const first = Array.from({ length: 100 }, (_, index) => ({ id: String(index), patrolStandardName: `S${index}` }))
    const fetch = vi.fn().mockResolvedValueOnce(json({ items: first, total: 101 })).mockResolvedValueOnce(json({ items: [{ id: '100', patrolStandardName: 'S100' }], total: 101 })).mockResolvedValueOnce(json([{ key: 'dev-ipqa-1', displayName: 'Development IPQA 1' }]))
    vi.stubGlobal('fetch', fetch)
    expect((await patrolPlansApi.standards()).length).toBe(101)
    expect(fetch.mock.calls[1][0]).toContain('page=2')
    expect((await patrolPlansApi.assignees())[0].key).toBe('dev-ipqa-1')
  })
})
