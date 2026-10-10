import { authFetch } from '@/services/authFetch'

export interface UserRow {
  id: string; username: string; displayName: string; role: 'Admin' | 'PQE' | 'IPQA'
  isActive: boolean; mustChangePassword: boolean; lockoutEndUtc: string | null
  createdAtUtc: string; createdBy: string; updatedAtUtc: string; updatedBy: string
}
export interface UserFilters {
  username?: string; displayName?: string; role?: string; active?: boolean | null
  page: number; pageSize: number
}
export interface UserCreate {
  username: string; displayName: string; role: UserRow['role']; initialPassword: string; isActive: boolean
}
export class UserApiError extends Error {
  constructor(message: string, readonly status: number, readonly errors?: Record<string, string[]>) { super(message) }
}

async function request(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  const response = await authFetch(`/api/users${path}`, { ...init, headers })
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { title?: string; errors?: Record<string, string[]> }
    throw new UserApiError(Object.values(body.errors ?? {}).flat().join(' ') || body.title || `HTTP ${response.status}`,
      response.status, body.errors)
  }
  return response
}
export const usersApi = {
  async list(filters: UserFilters) {
    const query = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) })
    if (filters.username) query.set('username', filters.username)
    if (filters.displayName) query.set('displayName', filters.displayName)
    if (filters.role) query.set('role', filters.role)
    if (filters.active !== null && filters.active !== undefined) query.set('active', String(filters.active))
    return (await (await request(`?${query}`)).json()) as { items: UserRow[]; total: number; page: number; pageSize: number }
  },
  async create(input: UserCreate) { return (await (await request('', { method: 'POST', body: JSON.stringify(input) })).json()) as UserRow },
  async update(id: string, displayName: string, role: UserRow['role']) {
    return (await (await request(`/${id}`, { method: 'PUT', body: JSON.stringify({ displayName, role }) })).json()) as UserRow
  },
  async setActive(id: string, active: boolean) {
    return (await (await request(`/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' })).json()) as UserRow
  },
  async resetPassword(id: string, temporaryPassword: string) {
    return (await (await request(`/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ temporaryPassword }) })).json()) as UserRow
  },
  async unlock(id: string) { return (await (await request(`/${id}/unlock`, { method: 'POST' })).json()) as UserRow },
}
