import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authSession } from '@/services/authSession'
import { lineSharedApi, SharedPlanError, type SharedPlan, type SharedPlanDraft } from './lineSharedApi'
import { validateSharedPlan } from './lineSharedValidation'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
const draft: SharedPlanDraft = { planNo: '', planName: 'Synthetic', patrolStandardId: 'standard', lineId: 'line', rounds: ['Day', 'Night'], effectiveStartLocal: '2026-11-10T07:00', effectiveEndLocal: null, isEnabled: false }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
beforeEach(() => { authSession.initialized.value = true; authSession.accessToken.value = 'synthetic' })
afterEach(() => vi.unstubAllGlobals())
describe('R03-34 shared planning contracts', () => {
  it('validates actual eligible lines, unique rounds, bounded names and effective intervals', () => {
    expect(validateSharedPlan(draft, ['line', 'other'])).toBe(true)
    for (const bad of [{ ...draft, lineId: 'unconfigured' }, { ...draft, rounds: [] }, { ...draft, rounds: ['Day', 'Day'] },
      { ...draft, rounds: ['Evening'] }, { ...draft, planName: ' ' }, { ...draft, planNo: 'x'.repeat(51) },
      { ...draft, effectiveEndLocal: draft.effectiveStartLocal }]) expect(validateSharedPlan(bad, ['line'])).toBe(false)
  })
  it('uses separate authenticated endpoints with expected revision and without assignees or task execution', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json({ id: 'plan', ...draft })))
    vi.stubGlobal('fetch', fetch)
    await lineSharedApi.lines(); await lineSharedApi.list(2); await lineSharedApi.save(null, draft)
    await lineSharedApi.save('plan', { ...draft, expectedRevision: 7 })
    await lineSharedApi.toggle({ ...draft, id: 'plan', sharedPlanRevision: 7, isEnabled: true } as SharedPlan)
    expect(fetch.mock.calls.map(x => x[0])).toEqual(['/api/line-patrol-plans/lines', '/api/line-patrol-plans?page=2&pageSize=10', '/api/line-patrol-plans', '/api/line-patrol-plans/plan', '/api/line-patrol-plans/plan/disable'])
    expect(JSON.parse(fetch.mock.calls[3][1].body as string).expectedRevision).toBe(7)
    expect(JSON.parse(fetch.mock.calls[4][1].body as string)).toEqual({ expectedRevision: 7 })
    expect(fetch.mock.calls[2][1].body).not.toContain('assignee')
    expect((fetch.mock.calls[2][1].headers as Headers).get('Authorization')).toBe('Bearer synthetic')
  })
  it('does not retry a stale revision or mutate unsaved draft', async () => {
    const fetch = vi.fn().mockResolvedValue(json({ code: 'SharedPlanConflict' }, 409)); vi.stubGlobal('fetch', fetch)
    const unsaved = { ...draft, expectedRevision: 7 }
    await expect(lineSharedApi.save('plan', unsaved)).rejects.toMatchObject({ status: 409, code: 'SharedPlanConflict' })
    expect(fetch).toHaveBeenCalledTimes(1); expect(unsaved.expectedRevision).toBe(7)
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(lineSharedApi.lines()).rejects.toBeInstanceOf(SharedPlanError)
  })
  it('provides matching complete bilingual messages without claiming execution readiness', () => {
    expect(Object.keys(en.sharedPlan)).toEqual(Object.keys(zh.sharedPlan))
    expect(Object.values(en.sharedPlan).every(Boolean)).toBe(true); expect(Object.values(zh.sharedPlan).every(Boolean)).toBe(true)
    expect(en.sharedPlan.notice).toContain('read-only'); expect(zh.sharedPlan.notice).toContain('只读')
  })
})
