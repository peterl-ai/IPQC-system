import { authFetch } from '@/services/authFetch'
import { canReadPicRoster, canWritePicRoster } from '@/services/permissions'
import { isPic, isReceipt, matchesReceipt, type Pic, type PicIntent, type PicPage, type PicReceipt } from './model'

const base = '/api/admin/pic-roster'
const pageSize = 20

export class PicApiError extends Error {
  constructor(readonly status: number) {
    super('PIC API status ' + status)
  }
}

async function request(actorId: string, path: string, init?: RequestInit) {
  const response = await authFetch(base + path, { ...init, signal: AbortSignal.timeout(20_000) }, {
    authorize: user => user?.id === actorId && (init ? canWritePicRoster(user) : canReadPicRoster(user)),
    refreshOnUnauthorized: !init,
  })
  if (!response.ok) throw new PicApiError(response.status)
  return response
}

async function pageOf<T>(response: Response, page: number, isItem: (value: unknown) => value is T) {
  const body = await response.json() as PicPage<T> | null
  if (!body || body.page !== page || body.pageSize !== pageSize || typeof body.hasMore !== 'boolean' ||
    !Array.isArray(body.items) || body.items.length > pageSize || !body.items.every(isItem)) {
    throw new Error('Invalid PIC page')
  }
  return body
}

export const picRosterApi = {
  async list(actorId: string, page: number, active: boolean | undefined) {
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (active !== undefined) query.set('active', String(active))
    return pageOf(await request(actorId, '?' + query), page, isPic)
  },
  async current(actorId: string, id: string): Promise<Pic> {
    const value: unknown = await (await request(actorId, '/' + id)).json()
    if (!isPic(value) || value.picId !== id) throw new Error('Invalid current PIC')
    return value
  },
  async history(actorId: string, id: string, page: number) {
    const result = await pageOf(await request(actorId, '/' + id + '/history?page=' + page + '&pageSize=' + pageSize), page, isReceipt)
    if (result.items.some(r => r.picId !== id)) throw new Error('Invalid PIC history')
    return result
  },
  async receipt(intent: PicIntent): Promise<PicReceipt> {
    const value: unknown = await (await request(intent.actorId, '/requests/' + intent.requestId + '/receipt')).json()
    if (!isReceipt(value) || !matchesReceipt(value, intent)) throw new Error('Unexpected PIC receipt')
    return value
  },
  async mutate(intent: PicIntent): Promise<PicReceipt> {
    const { action, picId, requestId, expectedRevision, values } = intent
    const target = picId ? '/' + picId : ''
    const route = action === 'Activated' ? target + '/activate' : action === 'Deactivated' ? target + '/deactivate' : target
    let body: object
    switch (action) {
      case 'Created':
        body = { requestId, ...values }
        break
      case 'Updated':
        body = { requestId, expectedRevision, name: values.name, department: values.department }
        break
      default:
        body = { requestId, expectedRevision }
    }
    const response = await request(intent.actorId, route, {
      method: action === 'Updated' ? 'PUT' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    const result = await response.json() as { receipt?: unknown; replayOnly?: unknown } | null
    if (!result || typeof result.replayOnly !== 'boolean' || !isReceipt(result.receipt) ||
      !matchesReceipt(result.receipt, intent) ||
      response.status !== (action === 'Created' && !result.replayOnly ? 201 : 200)) {
      throw new Error('Unexpected PIC mutation result')
    }
    return result.receipt
  },
}
