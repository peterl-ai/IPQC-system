import type { PlanDraft } from './api'

export function validatePlanDraft(draft: PlanDraft): string | null {
  if (!draft.planName.trim()) return 'plan.nameRequired'
  if (!draft.patrolStandardId) return 'plan.standardRequired'
  if (!draft.effectiveStartLocal) return 'plan.startRequired'
  if (draft.effectiveEndLocal && draft.effectiveEndLocal <= draft.effectiveStartLocal) return 'plan.endAfterStart'
  if (draft.isEnabled && !draft.assigneeKey) return 'plan.assigneeRequired'
  if (draft.schedule.type === 'everyNHours' && (!Number.isInteger(draft.schedule.intervalHours) || (draft.schedule.intervalHours ?? 0) < 1 || (draft.schedule.intervalHours ?? 0) > 168)) return 'plan.intervalRequired'
  if (draft.schedule.type !== 'everyNHours' && (!draft.schedule.times?.length || draft.schedule.times.some((x) => !/^([01]\d|2[0-3]):[0-5]\d$/.test(x)))) return 'plan.timesRequired'
  if (draft.schedule.type === 'weekly' && !draft.schedule.daysOfWeek?.length) return 'plan.daysRequired'
  return null
}
