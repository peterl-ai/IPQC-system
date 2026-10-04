import { mockSession } from '@/services/mockSession'

export interface ScheduleDefinition {
  type: 'everyNHours' | 'daily' | 'weekly'
  intervalHours: number | null
  times: string[] | null
  daysOfWeek: string[] | null
}

export interface PlanDraft {
  planNo: string
  planName: string
  patrolStandardId: string
  factoryCode: string
  factoryName: string
  lineCode: string
  lineName: string
  isEnabled: boolean
  effectiveStartLocal: string
  effectiveEndLocal: string | null
  timeZoneId: string
  schedule: ScheduleDefinition
  assigneeKey: string | null
}

export interface Plan extends PlanDraft {
  id: string
  patrolStandardName: string
  updatedAtUtc: string
}

export interface Assignee { key: string; displayName: string }
export interface StandardOption { id: string; patrolStandardName: string; factoryCode: string; factoryName: string; lineCode: string; lineName: string }

export class PlanApiError extends Error {
  constructor(message: string, readonly status: number, readonly errors?: Record<string, string[]>) { super(message) }
}

const base = import.meta.env.VITE_API_BASE_URL || '/api'
async function request(path: string, options: RequestInit = {}): Promise<Response> {
  const headers = new Headers(options.headers)
  if (import.meta.env.DEV) headers.set('X-Dev-Role', mockSession.role.value)
  if (options.body) headers.set('Content-Type', 'application/json')
  let response: Response
  try { response = await fetch(`${base}${path}`, { ...options, headers }) }
  catch { throw new PlanApiError('API unavailable', 0) }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { title?: string; errors?: Record<string, string[]> }
    throw new PlanApiError(Object.values(body.errors || {}).flat().join(' ') || body.title || `HTTP ${response.status}`, response.status, body.errors)
  }
  return response
}

export const patrolPlansApi = {
  async list(filters: { planNo?: string; planName?: string; patrolStandardId?: string; factoryCode?: string; lineCode?: string; enabled?: boolean | null; page: number; pageSize: number }) {
    const query = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) })
    for (const key of ['planNo', 'planName', 'patrolStandardId', 'factoryCode', 'lineCode'] as const) {
      if (filters[key]) query.set(key, filters[key]!)
    }
    if (filters.enabled !== null && filters.enabled !== undefined) query.set('enabled', String(filters.enabled))
    return (await (await request(`/patrol-plans?${query}`)).json()) as { items: Plan[]; page: number; pageSize: number; total: number }
  },
  async get(id: string) { return (await (await request(`/patrol-plans/${id}`)).json()) as Plan },
  async create(draft: PlanDraft) { return (await (await request('/patrol-plans', { method: 'POST', body: JSON.stringify(draft) })).json()) as Plan },
  async update(id: string, draft: PlanDraft) { return (await (await request(`/patrol-plans/${id}`, { method: 'PUT', body: JSON.stringify(draft) })).json()) as Plan },
  async setEnabled(id: string, enabled: boolean) { return (await (await request(`/patrol-plans/${id}/${enabled ? 'enable' : 'disable'}`, { method: 'POST' })).json()) as Plan },
  async remove(id: string) { await request(`/patrol-plans/${id}`, { method: 'DELETE' }) },
  async assignees() { return (await (await request('/patrol-plans/assignees')).json()) as Assignee[] },
  async standards() {
    const result: StandardOption[] = []
    for (let page = 1; ; page++) {
      const data = (await (await request(`/patrol-standards?page=${page}&pageSize=100`)).json()) as { items: StandardOption[]; total: number }
      result.push(...data.items)
      if (result.length >= data.total || data.items.length === 0) return result
    }
  },
}
