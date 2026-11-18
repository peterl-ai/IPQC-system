export interface Pic {
  picId: string
  revision: number
  name: string
  department: string
  isActive: boolean
}

export type PicAction = 'Created' | 'Updated' | 'Activated' | 'Deactivated'

export interface PicReceipt extends Pic {
  requestId: string
  contractVersion: 1
  action: PicAction
  actorUserId: string
  actorPersonalSessionId: string
  actorUsername: string
  environmentId: string
  occurredAtUtc: string
}

export interface PicPage<T> {
  items: T[]
  page: number
  pageSize: number
  hasMore: boolean
}

export interface PicValues {
  name: string
  department: string
  isActive: boolean
}

// Persist the frozen command and the expected receipt snapshot. The latter lets
// us also verify the fields deliberately omitted from an edit or lifecycle body.
export interface PicIntent {
  actorId: string
  requestId: string
  action: PicAction
  picId: string | null
  expectedRevision: number
  values: PicValues
}

const actions: readonly PicAction[] = ['Created', 'Updated', 'Activated', 'Deactivated']
export const isGuid = (v: unknown): v is string => typeof v === 'string' &&
  /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'

const positiveInteger = (v: number) => Number.isSafeInteger(v) && v > 0

export function normalizePicText(input: string): string | null {
  if (input.length > 400 || /[\p{Cc}\p{Cs}]/u.test(input)) return null
  // Match .NET Trim: Unicode space separators remain trimmable after controls
  // are rejected. JavaScript trim would additionally remove U+FEFF.
  const value = input.replace(/^\p{Z}+|\p{Z}+$/gu, '')
  return value && [...value].length <= 200 ? value : null
}

function isValues(v: Partial<PicValues> | null | undefined): v is PicValues {
  return !!v && typeof v.name === 'string' && normalizePicText(v.name) === v.name &&
    typeof v.department === 'string' && normalizePicText(v.department) === v.department &&
    typeof v.isActive === 'boolean'
}

export function isPic(value: unknown): value is Pic {
  const pic = value as Pic | null
  return isValues(pic) && isGuid(pic.picId) && positiveInteger(pic.revision)
}

export function isReceipt(value: unknown): value is PicReceipt {
  const r = value as PicReceipt | null
  return isPic(r) && isGuid(r.requestId) && r.contractVersion === 1 && actions.includes(r.action) &&
    isGuid(r.actorUserId) && isGuid(r.actorPersonalSessionId) &&
    typeof r.actorUsername === 'string' && r.actorUsername.length > 0 &&
    typeof r.environmentId === 'string' && r.environmentId.length > 0 &&
    typeof r.occurredAtUtc === 'string' && r.occurredAtUtc.endsWith('Z') && Number.isFinite(Date.parse(r.occurredAtUtc))
}

export function matchesReceipt(receipt: PicReceipt, intent: PicIntent): boolean {
  return receipt.requestId === intent.requestId && receipt.actorUserId === intent.actorId &&
    receipt.action === intent.action && (intent.picId === null || receipt.picId === intent.picId) &&
    receipt.revision === intent.expectedRevision + 1 && receipt.name === intent.values.name &&
    receipt.department === intent.values.department && receipt.isActive === intent.values.isActive
}

export function intentKey(actorId: string) {
  return 'ipqc-pic-roster-request-v1:' + actorId
}

export function readIntent(actorId: string): PicIntent | null {
  const raw = sessionStorage.getItem(intentKey(actorId))
  if (raw === null) return null
  const intent = JSON.parse(raw) as PicIntent | null
  if (!intent || intent.actorId !== actorId || !isGuid(actorId) || !isGuid(intent.requestId) ||
    !actions.includes(intent.action) || !isValues(intent.values) ||
    Object.keys(intent).sort().join() !== 'action,actorId,expectedRevision,picId,requestId,values' ||
    Object.keys(intent.values).sort().join() !== 'department,isActive,name' ||
    (intent.action === 'Created'
      ? intent.picId !== null || intent.expectedRevision !== 0
      : !isGuid(intent.picId) || !positiveInteger(intent.expectedRevision) ||
        intent.expectedRevision >= Number.MAX_SAFE_INTEGER) ||
    (intent.action === 'Activated' && !intent.values.isActive) ||
    (intent.action === 'Deactivated' && intent.values.isActive)) {
    throw new Error('Invalid stored PIC operation')
  }
  return intent
}

export function retainIntent(intent: PicIntent) {
  if (!isGuid(intent.actorId) || !isGuid(intent.requestId) || readIntent(intent.actorId)) {
    throw new Error('PIC operation cannot be stored')
  }
  sessionStorage.setItem(intentKey(intent.actorId), JSON.stringify(intent))
  requireRetainedIntent(intent)
}

export function requireRetainedIntent(intent: PicIntent) {
  if (JSON.stringify(readIntent(intent.actorId)) !== JSON.stringify(intent)) {
    throw new Error('Stored PIC operation does not match')
  }
}

export function releaseIntent(intent: PicIntent) {
  requireRetainedIntent(intent)
  sessionStorage.removeItem(intentKey(intent.actorId))
  if (readIntent(intent.actorId)) throw new Error('PIC operation was not removed')
}

export const picTime = (value: string, locale: string) => new Date(value).toLocaleString(
  locale === 'en' ? 'en-US' : 'zh-CN',
  { timeZone: 'America/New_York', timeZoneName: 'short' },
)
