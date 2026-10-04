import { describe, expect, it } from 'vitest'
import type { PlanDraft } from './api'
import { validatePlanDraft } from './validation'

const draft = (): PlanDraft => ({ planNo: '', planName: 'Line A', patrolStandardId: 'standard-id', factoryCode: '', factoryName: '', lineCode: '', lineName: '', isEnabled: true, effectiveStartLocal: '2026-10-03T08:00', effectiveEndLocal: null, timeZoneId: 'America/New_York', schedule: { type: 'everyNHours', intervalHours: 2, times: null, daysOfWeek: null }, assigneeKey: 'dev-ipqa-1' })

describe('Patrol Plan editor validation', () => {
  it('requires name, persisted standard, start, and enabled assignee', () => {
    expect(validatePlanDraft({ ...draft(), planName: '  ' })).toBe('plan.nameRequired')
    expect(validatePlanDraft({ ...draft(), patrolStandardId: '' })).toBe('plan.standardRequired')
    expect(validatePlanDraft({ ...draft(), effectiveStartLocal: '' })).toBe('plan.startRequired')
    expect(validatePlanDraft({ ...draft(), assigneeKey: null })).toBe('plan.assigneeRequired')
    expect(validatePlanDraft({ ...draft(), isEnabled: false, assigneeKey: null })).toBeNull()
  })
  it('validates effective range and all three frequency editors', () => {
    expect(validatePlanDraft({ ...draft(), effectiveEndLocal: '2026-10-03T08:00' })).toBe('plan.endAfterStart')
    expect(validatePlanDraft({ ...draft(), schedule: { type: 'everyNHours', intervalHours: 0, times: null, daysOfWeek: null } })).toBe('plan.intervalRequired')
    expect(validatePlanDraft({ ...draft(), schedule: { type: 'daily', intervalHours: null, times: [], daysOfWeek: null } })).toBe('plan.timesRequired')
    expect(validatePlanDraft({ ...draft(), schedule: { type: 'daily', intervalHours: null, times: ['08:00'], daysOfWeek: null } })).toBeNull()
    expect(validatePlanDraft({ ...draft(), schedule: { type: 'weekly', intervalHours: null, times: ['08:00'], daysOfWeek: [] } })).toBe('plan.daysRequired')
    expect(validatePlanDraft({ ...draft(), schedule: { type: 'weekly', intervalHours: null, times: ['08:00'], daysOfWeek: ['Monday'] } })).toBeNull()
  })
})
