import { authFetch } from '@/services/authFetch'

export interface SharedLine { id: string; code: string; name: string; selectionOrder: number }
export interface SharedPlanDraft {
  planNo: string; planName: string; patrolStandardId: string; lineId: string
  rounds: string[]; effectiveStartLocal: string; effectiveEndLocal: string | null
  isEnabled: boolean; expectedRevision?: number
}
export interface SharedPlan extends Omit<SharedPlanDraft, 'lineId'> {
  id: string; lineId: string | null; sharedPlanRevision: number | null
  configurationStatus: string; generationNotBeforeUtc: string
}
export class SharedPlanError extends Error {
  constructor(readonly code: string, readonly status: number) { super(code) }
}
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers)
  if (options.body) headers.set('Content-Type', 'application/json')
  let response: Response
  try { response = await authFetch(`/api/line-patrol-plans${path}`, { ...options, headers }) }
  catch { throw new SharedPlanError('Unavailable', 0) }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { code?: string }
    throw new SharedPlanError(body.code ?? 'Unavailable', response.status)
  }
  return await response.json() as T
}
export const lineSharedApi = {
  lines: () => request<SharedLine[]>('/lines'),
  list: (page = 1) => request<{ items: SharedPlan[]; total: number }>(`?page=${page}&pageSize=10`),
  save: (id: string | null, draft: SharedPlanDraft) => request<SharedPlan>(id ? `/${id}` : '', { method: id ? 'PUT' : 'POST', body: JSON.stringify(draft) }),
  toggle: (row: SharedPlan) => request<SharedPlan>(`/${row.id}/${row.isEnabled ? 'disable' : 'enable'}`, { method: 'POST', body: JSON.stringify({ expectedRevision: row.sharedPlanRevision }) }),
}
