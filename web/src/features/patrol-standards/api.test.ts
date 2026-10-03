import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockSession } from '@/services/mockSession'
import { ApiError, patrolStandardsApi } from './api'

const row = {
  id: '00000000-0000-4000-8000-000000000001', patrolStandardName: 'Standard A',
  factoryCode: 'F1', factoryName: '', workshopCode: '', lineCode: 'L1', lineName: 'Line 1',
  materialCode: '', createdBy: 'admin', createdAtUtc: '2026-10-03T12:00:00Z',
  updatedBy: 'admin', updatedAtUtc: '2026-10-03T12:00:00Z', inspectionItems: [{
    id: '00000000-0000-4000-8000-000000000002', sequenceNo: 1, processCode: 'P1',
    processName: '', inspectionItemCategory: 'Check', inspectionItem: 'Visual',
    inspectionContent: '', upperLimitOperator: '', upperLimitValue: '',
    lowerLimitOperator: '', lowerLimitValue: '', inspectionType: '', samplingPlan: '',
    sampleCount: '', photoRequirement: '', defectLevel: '',
  }],
}

afterEach(() => vi.unstubAllGlobals())

describe('Patrol Standards API client', () => {
  it('maps server DTOs and sends filters/pagination and development role', async () => {
    mockSession.setRole('pqe')
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [row], total: 1, page: 2, pageSize: 5 }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    const result = await patrolStandardsApi.list({ name: 'Standard', factory: 'F1', line: 'L1', page: 2, pageSize: 5 })
    expect(result.items[0].name).toBe('Standard A')
    expect(result.items[0].inspectionItems[0].itemCategory).toBe('Check')
    expect(fetch.mock.calls[0][0]).toContain('standardName=Standard&factoryCode=F1&lineCode=L1')
    expect((fetch.mock.calls[0][1].headers as Headers).get('X-Dev-Role')).toBe('pqe')
  })

  it('does not claim success on server validation failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { LineName: ['Line Name is required.'] } }), { status: 400 })))
    await expect(patrolStandardsApi.create({ name: '', factoryCode: '', factoryName: '', workshopCode: '',
      lineCode: '', lineName: '', materialCode: '', inspectionItems: [] })).rejects.toThrow('Line Name is required.')
  })

  it('reports network failure and does not fall back to mock persistence', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(patrolStandardsApi.list({ name: '', page: 1, pageSize: 10 })).rejects.toBeInstanceOf(ApiError)
  })

  it('uses the backend copy endpoint', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(row), { status: 201 }))
    vi.stubGlobal('fetch', fetch)
    await patrolStandardsApi.copy(row.id, 'Standard A (Copy)')
    expect(fetch.mock.calls[0][0]).toContain(`/${row.id}/copy`)
    expect(fetch.mock.calls[0][1].method).toBe('POST')
  })
})
