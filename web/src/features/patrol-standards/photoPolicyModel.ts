import type { PatrolStandard } from './model'

export type PhotoPolicyValue = 'Required' | 'Optional'
export interface PhotoPolicyInput {
  requestId: string
  canonicalizationVersion: 1
  expectedDefinitionId: string | null
  expectedDefinitionHash: string
  expectedConfirmationId: string | null
  value: PhotoPolicyValue
}
export interface PhotoPolicyReceipt {
  confirmationId: string; requestId: string; standardId: string; standardItemId: string
  definitionId: string; definitionVersion: number; canonicalizationVersion: number
  definitionHash: string; policyVersion: number; value: PhotoPolicyValue
  confirmedByUserId: string; confirmedPersonalSessionId: string; environmentId: string; confirmedAtUtc: string
}
export interface PhotoPolicyItem {
  standardId: string; standardItemId: string; rawPhotoRequirement: string
  definitionId: string | null; definitionVersion: number | null; definitionHash: string
  state: string; confirmation: PhotoPolicyReceipt | null
}
export interface PhotoPolicyPage { standardId: string; items: PhotoPolicyItem[]; reviewStandard: PatrolStandard | null }
export interface PhotoPolicyIntent { actorId: string; standardId: string; itemId: string; input: PhotoPolicyInput }

const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const hash = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v)
export const isPolicyValue = (v: unknown): v is PhotoPolicyValue => v === 'Required' || v === 'Optional'

// Only a non-secret frozen request is retained. Never save tokens, family credentials or a client approval.
export const intentKey = (actor: string, standard: string) => `ipqc-photo-policy-request-v1:${actor}:${standard}`
export const retainIntent = (v: PhotoPolicyIntent) => sessionStorage.setItem(intentKey(v.actorId, v.standardId), JSON.stringify(v))
export const removeIntent = (v: PhotoPolicyIntent) => sessionStorage.removeItem(intentKey(v.actorId, v.standardId))
export function readIntent(actor: string, standard: string): PhotoPolicyIntent | null {
  const raw = sessionStorage.getItem(intentKey(actor, standard))
  if (raw === null) return null
  const v = JSON.parse(raw) as PhotoPolicyIntent
  const i = v?.input
  if (!v || v.actorId !== actor || v.standardId !== standard || !guid(actor) || !guid(standard) || !guid(v.itemId) || !i ||
      Object.keys(v).sort().join() !== 'actorId,input,itemId,standardId' ||
      Object.keys(i).sort().join() !== 'canonicalizationVersion,expectedConfirmationId,expectedDefinitionHash,expectedDefinitionId,requestId,value' ||
      !guid(i.requestId) || i.canonicalizationVersion !== 1 || !hash(i.expectedDefinitionHash) || !isPolicyValue(i.value) ||
      !(i.expectedDefinitionId === null || guid(i.expectedDefinitionId)) || !(i.expectedConfirmationId === null || guid(i.expectedConfirmationId)))
    throw new Error('Invalid saved photo policy request')
  return v
}
export function matchesReceipt(r: PhotoPolicyReceipt, v: PhotoPolicyIntent): boolean {
  const i = v.input
  return !!r && guid(r.requestId) && guid(r.confirmationId) && guid(r.definitionId) && guid(r.confirmedByUserId) && guid(r.confirmedPersonalSessionId) &&
    r.requestId === i.requestId && r.standardId === v.standardId && r.standardItemId === v.itemId && r.value === i.value &&
    r.definitionHash?.toUpperCase() === i.expectedDefinitionHash.toUpperCase() && r.canonicalizationVersion === 1 &&
    (i.expectedDefinitionId === null || r.definitionId === i.expectedDefinitionId) && r.confirmedByUserId === v.actorId &&
    Number.isSafeInteger(r.policyVersion) && r.policyVersion > 0 && Number.isSafeInteger(r.definitionVersion) && r.definitionVersion > 0 &&
    typeof r.environmentId === 'string' && r.environmentId.length > 0 && Number.isFinite(Date.parse(r.confirmedAtUtc))
}
export function validReview(page: PhotoPolicyPage, standard: string): boolean {
  const s = page.reviewStandard
  return page.standardId === standard && s?.id === standard && Array.isArray(page.items) && Array.isArray(s.inspectionItems) &&
    page.items.length === s.inspectionItems.length && new Set(page.items.map(x => x.standardItemId)).size === page.items.length &&
    page.items.every(x => x.standardId === standard && guid(x.standardItemId) && hash(x.definitionHash) &&
      (x.definitionId === null ? x.definitionVersion === null : guid(x.definitionId) && Number.isSafeInteger(x.definitionVersion) && x.definitionVersion! > 0) &&
      (x.state === 'ExplicitlyConfirmed' ? !!x.confirmation && x.confirmation.definitionVersion === x.definitionVersion && matchesReceipt(x.confirmation, {
        actorId: x.confirmation.confirmedByUserId, standardId: standard, itemId: x.standardItemId, input: { requestId: x.confirmation.requestId, canonicalizationVersion: 1,
          expectedDefinitionId: x.definitionId, expectedDefinitionHash: x.definitionHash, expectedConfirmationId: null, value: x.confirmation.value },
      }) && isPolicyValue(x.confirmation.value) : x.confirmation === null) &&
      s.inspectionItems.some(i => i.id === x.standardItemId && i.photoRequirement === x.rawPhotoRequirement))
}
