import { mockSession } from '@/services/mockSession'

export type TaskStatus = 'PendingInspection' | 'InProgress' | 'PendingApproval' | 'Rejected' | 'Completed'
export interface TaskSummary {
  id: string
  taskNo: string
  status: TaskStatus
  scheduledOccurrenceUtc: string
  generatedAtUtc: string
  submittedAtUtc: string | null
  overallInspectionResult: 'Qualified' | 'Unqualified' | null
  assignedInspectorKey: string
  planNoSnapshot: string | null
  planNameSnapshot: string | null
  standardNameSnapshot: string | null
  factoryNameSnapshot: string | null
  lineNameSnapshot: string | null
  shift: 'Day' | 'Night' | null
  currentRevisionNo: number
}
export interface TaskSample {
  id: string
  sequenceNo: number
  inspectionValue: number | null
  judgmentResult: string | null
  inspectedAtUtc: string | null
}
export interface TaskItem {
  id: string
  sequenceNo: number
  processCode: string
  processName: string
  inspectionItemCategory: string
  inspectionItem: string
  inspectionContent: string
  inspectionType: string
  upperLimitOperator: string
  upperLimitValue: string
  lowerLimitOperator: string
  lowerLimitValue: string
  samplingPlan: string
  sampleCount: string
  photoRequirement: string
  isNa: boolean | null
  judgmentResult: string | null
  inspectedAtUtc: string | null
  remarks: string | null
  samples: TaskSample[]
}
export interface SubmissionSample { sequenceNo: number; inspectionValue: number | null; judgmentResult: string; inspectedAtUtc: string | null }
export interface SubmissionItem {
  patrolTaskItemId: string
  sequenceNo: number
  isNa: boolean
  judgmentResult: string
  inspectedAtUtc: string
  remarks: string | null
  samples: SubmissionSample[]
}
export interface Submission {
  revisionNo: number
  shift: string
  overallInspectionResult: string
  submittedBy: string
  submittedAtUtc: string
  items: SubmissionItem[]
  review: { revisionNo: number; reviewer: string; decision: string; reason: string | null; reviewedAtUtc: string } | null
}
export interface TaskDetail {
  summary: TaskSummary
  factoryCodeSnapshot: string | null
  workshopCodeSnapshot: string | null
  lineCodeSnapshot: string | null
  materialCodeSnapshot: string | null
  sourceStandardUpdatedAtUtc: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  items: TaskItem[]
  submissions: Submission[]
}
export interface TaskFilters {
  taskNo?: string
  status?: string
  planId?: string
  standardId?: string
  factory?: string
  line?: string
  inspector?: string
  scheduledFrom?: string
  scheduledTo?: string
  page: number
  pageSize: number
}

export class TaskApiError extends Error {
  constructor(message: string, readonly status: number) { super(message) }
}

export function canSelectForApproval(task: TaskSummary): boolean { return task.status === 'PendingApproval' }
export function validRejectReason(reason: string): boolean { return reason.trim().length > 0 }

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      ...init,
      headers: { 'Content-Type': 'application/json', 'X-Dev-Role': mockSession.role.value, ...init?.headers },
    })
  } catch { throw new TaskApiError('API unavailable', 0) }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { title?: string; errors?: Record<string, string[]> }
    throw new TaskApiError(Object.values(body.errors ?? {}).flat()[0] || body.title || `HTTP ${response.status}`, response.status)
  }
  return response
}

export const patrolTasksApi = {
  async list(filters: TaskFilters) {
    const query = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) })
    for (const key of ['taskNo', 'status', 'planId', 'standardId', 'factory', 'line', 'inspector', 'scheduledFrom', 'scheduledTo'] as const) {
      if (filters[key]) query.set(key, filters[key]!)
    }
    return (await (await request(`/patrol-tasks?${query}`)).json()) as { items: TaskSummary[]; page: number; pageSize: number; total: number }
  },
  async get(id: string) { return (await (await request(`/patrol-tasks/${id}`)).json()) as TaskDetail },
  async approve(id: string) { await request(`/patrol-tasks/${id}/approve`, { method: 'POST' }) },
  async reject(id: string, reason: string) { await request(`/patrol-tasks/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }) },
  async batchApprove(taskIds: string[]) { await request('/patrol-tasks/batch-approve', { method: 'POST', body: JSON.stringify({ taskIds }) }) },
}
