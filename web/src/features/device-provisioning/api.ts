import { authFetch } from '@/services/authFetch'

export interface Page<T> { items: T[]; total: number; page: number; pageSize: number }
export interface Line { id: string; code: string; name: string; isActive: boolean; revision: number }
export interface Device {
  id: string; displayName: string; model: string | null; serial: string | null
  status: 'PendingCredential' | 'Revoked' | 'Replaced'; lineId: string | null
  revision: number; bindingVersion: number; replacedByDeviceId: string | null
  createdAtUtc: string; updatedAtUtc: string; terminalAtUtc: string | null; terminalReason: string | null
  credentialStatus: 'NotEnrolled'; canExecuteLineShared: false
}
export interface Snapshot {
  name: string; revision: number; code: string | null; isActive: boolean | null
  model: string | null; serial: string | null; status: Device['status'] | null
  lineId: string | null; bindingVersion: number | null; replacedByDeviceId: string | null
}
export interface Audit {
  id: string; changeId: string; action: string; actorUserId: string; actorUsername: string
  occurredAtUtc: string; reason: string | null; before: Snapshot | null; after: Snapshot
}
export type DeviceAction = 'bind' | 'rebind' | 'unbind' | 'revoke' | 'replace'
export class ProvisioningApiError extends Error {
  constructor(readonly code: string, readonly status: number) { super(code) }
}
const knownErrors = ['InvalidInput', 'Conflict', 'NotFound', 'DuplicateCode', 'LineInUse', 'InactiveLine', 'LineOccupied', 'TerminalDevice', 'InvalidTransition']
export function errorCode(error: unknown): string {
  if (!(error instanceof ProvisioningApiError)) return 'Unavailable'
  if (error.status === 401 || error.status === 403) return 'Forbidden'
  return knownErrors.includes(error.code) ? error.code : error.status === 400 ? 'InvalidInput' : 'Unavailable'
}
async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await authFetch(`/api/admin${path}`, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { code?: string }
    throw new ProvisioningApiError(problem.code ?? '', response.status)
  }
  return await response.json() as T
}
async function all<T>(load: (page: number) => Promise<Page<T>>): Promise<T[]> {
  const rows: T[] = []
  for (let page = 1; ; page++) {
    const result = await load(page); rows.push(...result.items)
    if (rows.length >= result.total || result.items.length === 0) return rows
  }
}
export const provisioningApi = {
  lines: (page = 1, pageSize = 20) => request<Page<Line>>(`/production-lines?page=${page}&pageSize=${pageSize}`),
  devices: (page = 1, pageSize = 20) => request<Page<Device>>(`/devices?page=${page}&pageSize=${pageSize}`),
  lineOptions: () => all((page) => provisioningApi.lines(page, 100)),
  deviceOptions: () => all((page) => provisioningApi.devices(page, 100)),
  createLine: (code: string, name: string) => request<Line>('/production-lines', 'POST', { code, name, isActive: true }),
  renameLine: (line: Line, name: string) => request<Line>(`/production-lines/${line.id}`, 'PUT', { name, expectedRevision: line.revision }),
  setLineActive: (line: Line, active: boolean) => request<Line>(`/production-lines/${line.id}/${active ? 'activate' : 'deactivate'}`, 'POST', { expectedRevision: line.revision }),
  createDevice: (displayName: string, model: string, serial: string) => request<Device>('/devices', 'POST', { displayName, model, serial }),
  editDevice: (device: Device, displayName: string, model: string, serial: string) => request<Device>(`/devices/${device.id}`, 'PUT', { displayName, model, serial, expectedRevision: device.revision }),
  deviceAction: (device: Device, action: DeviceAction, input: { lineId?: string; reason?: string; replacementDeviceId?: string; replacementExpectedRevision?: number }) =>
    request<Device>(`/devices/${device.id}/${action}`, 'POST', { ...input, expectedRevision: device.revision }),
  audit: (kind: 'production-lines' | 'devices', id: string, page = 1) => request<Page<Audit>>(`/${kind}/${id}/audit?page=${page}&pageSize=20`),
}
