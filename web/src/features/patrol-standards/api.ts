import { mockSession } from '@/services/mockSession'
import type { PatrolStandard, PatrolStandardDraft, PatrolStandardItem, PatrolStandardSummary } from './model'

interface ApiItem extends Omit<PatrolStandardItem, 'id' | 'itemCategory' | 'upperOperator' | 'upperValue' | 'lowerOperator' | 'lowerValue'> {
  id: string
  inspectionItemCategory: string
  upperLimitOperator: string
  upperLimitValue: string
  lowerLimitOperator: string
  lowerLimitValue: string
  sequenceNo: number
}

interface ApiSummary extends Omit<PatrolStandardSummary, 'name' | 'createdTime' | 'updatedTime'> {
  patrolStandardName: string
  createdAtUtc: string
  updatedAtUtc: string
}

interface ApiStandard extends ApiSummary {
  inspectionItems: ApiItem[]
}

interface ApiPage { items: ApiSummary[]; page: number; pageSize: number; total: number }

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly errors?: Record<string, string[]>) { super(message) }
}

const base = import.meta.env.VITE_API_BASE_URL || '/api'

async function request(path: string, options: RequestInit = {}): Promise<Response> {
  const headers = new Headers(options.headers)
  if (import.meta.env.DEV) headers.set('X-Dev-Role', mockSession.role.value)
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  let response: Response
  try { response = await fetch(`${base}${path}`, { ...options, headers }) }
  catch { throw new ApiError('Cannot connect to the Patrol Standards API.', 0) }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { title?: string; errors?: Record<string, string[]> }
    const validation = body.errors && Object.values(body.errors).flat().join(' ')
    throw new ApiError(validation || body.title || `Request failed (${response.status}).`, response.status, body.errors)
  }
  return response
}

function mapItem(item: ApiItem): PatrolStandardItem {
  return { ...item, itemCategory: item.inspectionItemCategory, upperOperator: item.upperLimitOperator,
    upperValue: item.upperLimitValue, lowerOperator: item.lowerLimitOperator, lowerValue: item.lowerLimitValue }
}

function mapStandard(row: ApiStandard): PatrolStandard {
  return { ...mapSummary(row), inspectionItems: row.inspectionItems.map(mapItem) }
}

function mapSummary(row: ApiSummary): PatrolStandardSummary {
  return { id: row.id, name: row.patrolStandardName, factoryCode: row.factoryCode,
    factoryName: row.factoryName, workshopCode: row.workshopCode, lineCode: row.lineCode,
    lineName: row.lineName, materialCode: row.materialCode, createdBy: row.createdBy,
    createdTime: new Date(row.createdAtUtc).toLocaleString(), updatedBy: row.updatedBy,
    updatedTime: new Date(row.updatedAtUtc).toLocaleString() }
}

function payload(draft: PatrolStandardDraft) {
  return { patrolStandardName: draft.name, factoryCode: draft.factoryCode,
    factoryName: draft.factoryName, workshopCode: draft.workshopCode, lineCode: draft.lineCode,
    lineName: draft.lineName, materialCode: draft.materialCode,
    inspectionItems: draft.inspectionItems.map((item) => ({
      id: item.id.startsWith('local:') ? null : item.id,
      processCode: item.processCode, processName: item.processName,
      inspectionItemCategory: item.itemCategory, inspectionItem: item.inspectionItem,
      inspectionContent: item.inspectionContent, upperLimitOperator: item.upperOperator,
      upperLimitValue: item.upperValue, lowerLimitOperator: item.lowerOperator,
      lowerLimitValue: item.lowerValue, inspectionType: item.inspectionType,
      samplingPlan: item.samplingPlan, sampleCount: item.sampleCount,
      photoRequirement: item.photoRequirement, defectLevel: item.defectLevel,
    })) }
}

export const patrolStandardsApi = {
  async list(params: { name: string; factory?: string; line?: string; page: number; pageSize: number }) {
    const query = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize) })
    if (params.name) query.set('standardName', params.name)
    if (params.factory) query.set('factoryCode', params.factory)
    if (params.line) query.set('lineCode', params.line)
    const data = await (await request(`/patrol-standards?${query}`)).json() as ApiPage
    return { ...data, items: data.items.map(mapSummary) }
  },
  async get(id: string) { return mapStandard(await (await request(`/patrol-standards/${id}`)).json() as ApiStandard) },
  async create(draft: PatrolStandardDraft) {
    return mapStandard(await (await request('/patrol-standards', { method: 'POST', body: JSON.stringify(payload(draft)) })).json() as ApiStandard)
  },
  async update(id: string, draft: PatrolStandardDraft) {
    return mapStandard(await (await request(`/patrol-standards/${id}`, { method: 'PUT', body: JSON.stringify(payload(draft)) })).json() as ApiStandard)
  },
  async remove(id: string) { await request(`/patrol-standards/${id}`, { method: 'DELETE' }) },
  async copy(id: string, name: string) {
    return mapStandard(await (await request(`/patrol-standards/${id}/copy`, { method: 'POST', body: JSON.stringify({ patrolStandardName: name }) })).json() as ApiStandard)
  },
  async import(file: File) {
    const form = new FormData()
    form.append('file', file)
    return mapStandard(await (await request('/patrol-standards/import', { method: 'POST', body: form })).json() as ApiStandard)
  },
  async template() { return (await request('/patrol-standards/template')).blob() },
  async export(id: string) { return (await request(`/patrol-standards/export?id=${encodeURIComponent(id)}`)).blob() },
}
