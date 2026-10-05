import { mockSession } from '@/services/mockSession'

export interface CompletedRecordSummary {
  id: string; taskNo: string; completedAtUtc: string | null; scheduledOccurrenceUtc: string
  submittedAtUtc: string | null; overallInspectionResult: 'Qualified' | 'Unqualified' | null
  finalRevisionNo: number; inspector: string; shift: string | null; planNo: string | null
  planName: string | null; standardName: string | null; factoryCode: string | null
  factoryName: string | null; workshopCode: string | null; lineCode: string | null
  lineName: string | null; materialCode: string | null; approvedBy: string | null
  approvedAtUtc: string | null
}
export interface CompletedRecordSample {
  sequenceNo: number; inspectionValue: number | null; judgmentResult: string; inspectedAtUtc: string | null
}
export interface CompletedRecordItem {
  sequenceNo: number; processCode: string; processName: string; inspectionItemCategory: string
  inspectionItem: string; inspectionContent: string; inspectionType: string
  lowerLimitOperator: string; lowerLimitValue: string; upperLimitOperator: string; upperLimitValue: string
  samplingPlan: string; sampleCount: string; photoRequirement: string; defectLevel: string
  isNa: boolean; judgmentResult: string; inspectedAtUtc: string; machineCode: string | null
  series: string | null; mold: string | null; abnormalType: string | null; abnormalCause: string | null
  remarks: string | null; samples: CompletedRecordSample[]
}
export interface CompletedRecordRevision {
  revisionNo: number; shift: string; overallInspectionResult: string; submittedBy: string
  submittedAtUtc: string; reviewDecision: string | null; reviewedBy: string | null
  reviewedAtUtc: string | null; rejectReason: string | null
  items: { sequenceNo: number; isNa: boolean; judgmentResult: string; inspectedAtUtc: string; samples: CompletedRecordSample[] }[]
}
export interface CompletedPatrolReport {
  summary: CompletedRecordSummary; generatedAtUtc: string; startedAtUtc: string | null
  finalRevisionNo: number; finalSubmittedBy: string; finalSubmittedAtUtc: string
  items: CompletedRecordItem[]; revisionHistory: CompletedRecordRevision[]
}
export interface RecordFilters {
  taskNo?: string; plan?: string; standard?: string; factory?: string; line?: string
  inspector?: string; shift?: string; result?: string; completedFrom?: string; completedTo?: string
  page: number; pageSize: number
}
export class CompletedRecordApiError extends Error {
  constructor(message: string, readonly status: number) { super(message) }
}
const base = import.meta.env.VITE_API_BASE_URL || '/api'
async function request(path: string): Promise<Response> {
  let response: Response
  try {
    response = await fetch(`${base}${path}`, { headers: import.meta.env.DEV ? { 'X-Dev-Role': mockSession.role.value } : {} })
  } catch { throw new CompletedRecordApiError('API unavailable', 0) }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { title?: string; errors?: Record<string, string[]> }
    throw new CompletedRecordApiError(Object.values(body.errors || {}).flat()[0] || body.title || `HTTP ${response.status}`, response.status)
  }
  return response
}
export const completedRecordsApi = {
  async list(filters: RecordFilters) {
    const query = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) })
    for (const key of ['taskNo', 'plan', 'standard', 'factory', 'line', 'inspector', 'shift', 'result', 'completedFrom', 'completedTo'] as const)
      if (filters[key]) query.set(key, filters[key]!)
    return (await (await request(`/completed-patrol-records?${query}`)).json()) as {
      items: CompletedRecordSummary[]; page: number; pageSize: number; total: number
    }
  },
  async get(id: string) { return (await (await request(`/completed-patrol-records/${id}`)).json()) as CompletedPatrolReport },
  async report(id: string) { return (await (await request(`/completed-patrol-records/${id}/report`)).json()) as CompletedPatrolReport },
  async download(id: string) { return (await request(`/completed-patrol-records/${id}/report.xlsx`)).blob() },
}
