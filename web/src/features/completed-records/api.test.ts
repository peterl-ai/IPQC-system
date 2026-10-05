import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mockSession } from '@/services/mockSession'
import { completedRecordsApi, CompletedRecordApiError } from './api'

const summary = { id: 'record-1', taskNo: 'PT-1', finalRevisionNo: 2, overallInspectionResult: 'Qualified' }
const report = { summary, finalRevisionNo: 2, items: [{ sequenceNo: 1, isNa: true, samples: [] }],
  revisionHistory: [{ revisionNo: 1, reviewDecision: 'Rejected', rejectReason: 'Recheck' },
    { revisionNo: 2, reviewDecision: 'Approved', rejectReason: null }] }
function json(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }) }

describe('completed records API', () => {
  beforeEach(() => { mockSession.setRole('admin'); vi.restoreAllMocks() })

  it('requests filtered pages from the completed-only API and preserves the response', async () => {
    const fetch = vi.fn().mockResolvedValue(json({ items: [summary], page: 2, pageSize: 1, total: 3 }))
    vi.stubGlobal('fetch', fetch)
    const data = await completedRecordsApi.list({ taskNo: 'PT', plan: 'Line', standard: 'Standard', factory: 'Factory',
      line: 'Line', inspector: 'ipqa', shift: 'Day', result: 'Qualified', completedFrom: '2026-03-08',
      completedTo: '2026-03-08', page: 2, pageSize: 1 })
    expect(data.total).toBe(3)
    expect(data.items[0].finalRevisionNo).toBe(2)
    const [url, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/completed-patrol-records?')
    expect(url).toContain('completedFrom=2026-03-08')
    expect(url).toContain('page=2&pageSize=1')
    expect(init.headers).toEqual({ 'X-Dev-Role': 'admin' })
  })

  it('loads detail and preview independently from the server report model', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json(report)))
    vi.stubGlobal('fetch', fetch)
    expect((await completedRecordsApi.get('record-1')).revisionHistory[0].rejectReason).toBe('Recheck')
    expect((await completedRecordsApi.report('record-1')).items[0].isNa).toBe(true)
    expect(fetch.mock.calls[0][0]).toBe('/api/completed-patrol-records/record-1')
    expect(fetch.mock.calls[1][0]).toBe('/api/completed-patrol-records/record-1/report')
  })

  it('downloads one XLSX as a blob with the selected development role', async () => {
    mockSession.setRole('ipqa')
    const fetch = vi.fn().mockResolvedValue(new Response(new Blob(['xlsx']), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect((await completedRecordsApi.download('record-1')).size).toBeGreaterThan(0)
    expect(fetch.mock.calls[0][0]).toBe('/api/completed-patrol-records/record-1/report.xlsx')
    expect((fetch.mock.calls[0][1] as RequestInit).headers).toEqual({ 'X-Dev-Role': 'ipqa' })
  })

  it('surfaces integrity conflicts and network failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Inconsistent record' }, 409)))
    await expect(completedRecordsApi.report('record-1')).rejects.toMatchObject({ status: 409 })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    await expect(completedRecordsApi.list({ page: 1, pageSize: 10 })).rejects.toBeInstanceOf(CompletedRecordApiError)
  })
})
