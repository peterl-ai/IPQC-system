import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockSession } from '@/services/mockSession'
import en from '@/i18n/locales/en'
import zhCN from '@/i18n/locales/zh-CN'
import { canSelectForApproval, patrolTasksApi, TaskApiError, validRejectReason, type TaskSummary } from './api'

const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const task = (status: TaskSummary['status']): TaskSummary => ({
  id: 'task-1', taskNo: 'PT-1', status, scheduledOccurrenceUtc: '2026-10-03T12:00:00Z',
  generatedAtUtc: '2026-10-03T12:00:00Z', submittedAtUtc: null, overallInspectionResult: null,
  assignedInspectorKey: 'dev-ipqa-1', planNoSnapshot: 'P-1', planNameSnapshot: 'Plan',
  standardNameSnapshot: 'Standard', factoryNameSnapshot: 'Factory', lineNameSnapshot: 'Line',
  shift: null, currentRevisionNo: 0,
})
afterEach(() => vi.unstubAllGlobals())

describe('Patrol Tasks API', () => {
  it('loads filtered pages and task detail from the persistent API', async () => {
    mockSession.setRole('pqe')
    const fetch = vi.fn().mockResolvedValueOnce(response({ items: [task('PendingInspection')], total: 1, page: 2, pageSize: 5 }))
      .mockResolvedValueOnce(response({ summary: task('PendingInspection'), items: [], submissions: [] }))
    vi.stubGlobal('fetch', fetch)
    const page = await patrolTasksApi.list({ taskNo: 'PT-1', status: 'Active', planId: 'plan-1', factory: 'Factory', page: 2, pageSize: 5 })
    expect(page.items[0].planNoSnapshot).toBe('P-1')
    const query = new URL(fetch.mock.calls[0][0], 'http://localhost').searchParams
    expect(Object.fromEntries(query)).toMatchObject({ taskNo: 'PT-1', status: 'Active', planId: 'plan-1', factory: 'Factory', page: '2', pageSize: '5' })
    expect((fetch.mock.calls[0][1].headers as Record<string, string>)['X-Dev-Role']).toBe('pqe')
    expect((await patrolTasksApi.get('task-1')).summary.standardNameSnapshot).toBe('Standard')
  })
  it('calls single approval, rejection, and batch approval endpoints', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    await patrolTasksApi.approve('task-1')
    await patrolTasksApi.reject('task-2', 'Check label')
    await patrolTasksApi.batchApprove(['task-3', 'task-4'])
    expect(fetch.mock.calls.map((x) => [x[0], x[1].method])).toEqual([
      ['/api/patrol-tasks/task-1/approve', 'POST'], ['/api/patrol-tasks/task-2/reject', 'POST'],
      ['/api/patrol-tasks/batch-approve', 'POST'],
    ])
    expect(JSON.parse(fetch.mock.calls[1][1].body as string)).toEqual({ reason: 'Check label' })
    expect(JSON.parse(fetch.mock.calls[2][1].body as string)).toEqual({ taskIds: ['task-3', 'task-4'] })
  })
  it('limits selection, validates rejection reason, and reports API conflicts', async () => {
    expect(canSelectForApproval(task('PendingApproval'))).toBe(true)
    for (const status of ['PendingInspection', 'InProgress', 'Rejected', 'Completed'] as const)
      expect(canSelectForApproval(task(status))).toBe(false)
    expect(validRejectReason('  ')).toBe(false)
    expect(validRejectReason(' Re-inspect ')).toBe(true)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ title: 'Already reviewed' }, 409)))
    await expect(patrolTasksApi.approve('task-1')).rejects.toMatchObject({ status: 409 })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(patrolTasksApi.get('task-1')).rejects.toBeInstanceOf(TaskApiError)
  })
  it('provides English and Chinese labels for task details and revision history', () => {
    for (const locale of [en, zhCN]) {
      expect(locale.task.status.PendingApproval).toBeTruthy()
      expect(locale.task.itemDetails).toBeTruthy()
      expect(locale.task.revisionHistory).toBeTruthy()
      expect(locale.task.reasonRequired).toBeTruthy()
      expect(locale.task.result.Unqualified).toBeTruthy()
    }
  })
})
