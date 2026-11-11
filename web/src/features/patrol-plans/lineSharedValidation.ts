import type { SharedPlanDraft } from './lineSharedApi'

export function validateSharedPlan(draft: SharedPlanDraft, lineIds: string[]): boolean {
  return draft.planName.trim().length > 0 && draft.planName.trim().length <= 200 && draft.planNo.trim().length <= 50 &&
    !!draft.patrolStandardId && lineIds.includes(draft.lineId) && draft.rounds.length > 0 && draft.rounds.length <= 2 &&
    new Set(draft.rounds).size === draft.rounds.length && draft.rounds.every(x => x === 'Day' || x === 'Night') &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(draft.effectiveStartLocal) &&
    (!draft.effectiveEndLocal || /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(draft.effectiveEndLocal) && draft.effectiveEndLocal > draft.effectiveStartLocal)
}
