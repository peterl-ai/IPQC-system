/* eslint-disable vue/one-component-per-file -- Test-local UI adapters. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App, type PropType } from 'vue'
import { createI18n } from 'vue-i18n'
import { authSession } from '@/services/authSession'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import Panel from './LineSharedPlansPanel.vue'
import { lineSharedApi, SharedPlanError, type SharedPlan, type SharedPlanDraft } from './lineSharedApi'
import { patrolPlansApi } from './api'
vi.mock('./lineSharedApi', async original => ({ ...await original<typeof import('./lineSharedApi')>(), lineSharedApi: { lines: vi.fn(), list: vi.fn(), get: vi.fn(), save: vi.fn(), toggle: vi.fn() } }))
vi.mock('./api', () => ({ patrolPlansApi: { standards: vi.fn() } }))
let app: App, host: HTMLDivElement
const flush = async () => { await new Promise(resolve => setTimeout(resolve, 0)); await nextTick() }
const row: SharedPlan = { id: 'plan', planNo: 'Synthetic', planName: 'Original', patrolStandardId: 's', lineId: 'line', rounds: ['Day'], effectiveStartLocal: '2026-11-10T07:00', effectiveEndLocal: null, isEnabled: false, sharedPlanRevision: 3, configurationStatus: 'Admitted', generationNotBeforeUtc: '' }
const latest = (): SharedPlan => ({ ...row, planName: 'Competing server name', lineId: 'line2', patrolStandardId: 'server-standard', rounds: ['Night'], isEnabled: true, effectiveStartLocal: '2026-11-12T07:00', sharedPlanRevision: 4 })
const button = (label: string) => [...host.querySelectorAll('button')].find(x => x.textContent === label)!
const acknowledge = (labels: typeof en | typeof zh, revision: number) => labels.sharedPlan.keepDraftAcknowledge.replace('{revision}', String(revision))
const nameInput = () => host.querySelectorAll('input')[1]!
async function click(label: string) { button(label).click(); await flush() }
async function typeName(value: string) { nameInput().value = value; nameInput().dispatchEvent(new Event('input')); await flush() }
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })
async function mount(locale = 'en') {
  host = document.createElement('div'); document.body.append(host)
  app = createApp(Panel); app.use(createI18n({ legacy: false, locale, messages: { en, 'zh-CN': zh } }))
  const container = defineComponent({ setup: (_, { slots }) => () => h('div', [slots.default?.(), slots.footer?.()]) })
  for (const name of ['ASpace', 'AForm', 'AFormItem', 'ACheckboxGroup', 'ASelect', 'ASwitch']) app.component(name, container)
  app.component('ADrawer', defineComponent({ props: { open: Boolean }, setup: (p, { slots }) => () => p.open ? h('aside', [slots.default?.(), slots.footer?.()]) : null }))
  app.component('AAlert', defineComponent({ props: { message: { type: String, default: '' } }, setup: (p, { attrs }) => () => h('p', attrs, p.message) }))
  app.component('AButton', defineComponent({ setup: (_, { slots, attrs }) => () => h('button', attrs, slots.default?.()) }))
  app.component('AInput', defineComponent({ props: { value: { type: String, default: '' } }, emits: ['update:value'], setup: (p, { emit }) => () => h('input', { value: p.value, onInput: (e: Event) => emit('update:value', (e.target as HTMLInputElement).value) }) }))
  app.component('ATable', defineComponent({ props: { dataSource: { type: Array as PropType<SharedPlan[]>, default: () => [] } }, setup: (p, { slots }) => () => h('div', { 'data-testid': 'plans-table' }, p.dataSource?.map(record => h('article', [String(record.sharedPlanRevision), record.planName, slots.bodyCell?.({ column: { key: 'status' }, record }), slots.bodyCell?.({ column: { key: 'actions' }, record })]))) }))
  app.mount(host); await flush()
}
beforeEach(() => {
  vi.resetAllMocks(); authSession.initialized.value = true; authSession.accessToken.value = 'synthetic-test-only'
  vi.mocked(lineSharedApi.lines).mockResolvedValue([{ id: 'line', code: 'L1', name: 'Synthetic 1', selectionOrder: 1 }, { id: 'line2', code: 'L2', name: 'Synthetic 2', selectionOrder: 2 }])
  vi.mocked(lineSharedApi.list).mockResolvedValue({ items: [{ ...row }], total: 1 }); vi.mocked(patrolPlansApi.standards).mockResolvedValue([])
  vi.mocked(lineSharedApi.get).mockResolvedValue(latest()); vi.mocked(lineSharedApi.toggle).mockResolvedValue({ ...row, isEnabled: true, sharedPlanRevision: 4 })
})
afterEach(() => { app?.unmount(); host?.remove(); vi.unstubAllGlobals() })
describe('R03-34 explicit shared Plan conflict reconciliation', () => {
  it.each(['en', 'zh-CN'])('handles actual HTTP409, preserves all draft values and requires explicit acknowledgement after each race (%s)', async locale => {
    const labels = locale === 'en' ? en : zh; let server = latest(); let writes = 0
    const actual = await vi.importActual<typeof import('./lineSharedApi')>('./lineSharedApi')
    vi.mocked(lineSharedApi.save).mockImplementation(actual.lineSharedApi.save); vi.mocked(lineSharedApi.get).mockImplementation(actual.lineSharedApi.get)
    const fetch = vi.fn(async (_url: string | URL | Request, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        writes++; if (writes <= 2) { if (writes === 2) server = { ...server, planName: 'Second competing change', sharedPlanRevision: 5 }; return json({ code: 'SharedPlanConflict', status: 409 }, 409) }
        const payload = JSON.parse(init.body as string) as SharedPlanDraft
        server = { ...server, ...payload, sharedPlanRevision: 6 }; return json(server)
      }
      return json(server)
    })
    vi.stubGlobal('fetch', fetch); vi.mocked(lineSharedApi.list).mockImplementation(async () => ({ items: [server], total: 1 }))
    // Start at the old revision3; the independent writer has already committed revision4.
    vi.mocked(lineSharedApi.list).mockResolvedValueOnce({ items: [row], total: 1 })
    await mount(locale); await click(labels.common.edit); await typeName('Unsaved contribution')
    await click(labels.action.save)
    expect(lineSharedApi.save).toHaveBeenCalledTimes(1); expect(lineSharedApi.get).toHaveBeenCalledWith('plan')
    expect(nameInput().value).toBe('Unsaved contribution'); expect(host.textContent).toContain('Competing server name')
    expect(host.querySelector('[data-testid=plans-table]')!.textContent).toContain(labels.common.enabled)
    expect(host.textContent).toContain(labels.sharedPlan.compareValues); expect(host.querySelectorAll('tr[data-different=true]').length).toBeGreaterThan(2)
    expect(button(labels.action.save).disabled).toBe(true)
    const first = vi.mocked(lineSharedApi.save).mock.calls[0]![1]
    expect(first).toMatchObject({ planName: 'Unsaved contribution', lineId: 'line', rounds: ['Day'], isEnabled: false, expectedRevision: 3 })
    await click(acknowledge(labels, 4)); expect(lineSharedApi.save).toHaveBeenCalledTimes(1)
    await click(labels.action.save)
    expect(lineSharedApi.save).toHaveBeenCalledTimes(2); expect(nameInput().value).toBe('Unsaved contribution')
    expect(vi.mocked(lineSharedApi.save).mock.calls[1]![1]).toEqual({ ...first, expectedRevision: 4 })
    expect(host.textContent).toContain('Second competing change'); expect(button(labels.action.save).disabled).toBe(true)
    await click(acknowledge(labels, 5)); expect(lineSharedApi.save).toHaveBeenCalledTimes(2)
    await click(labels.action.save)
    expect(vi.mocked(lineSharedApi.save).mock.calls[2]![1]).toEqual({ ...first, expectedRevision: 5 })
    expect(fetch.mock.calls.filter(x => x[1]?.method === 'PUT')).toHaveLength(3)
    expect(host.textContent).not.toContain('sharedPlan.'); expect(localStorage.length).toBe(0)
  })
  it('loads the server version only after an explicit discard action without an automatic write', async () => {
    vi.mocked(lineSharedApi.save).mockRejectedValueOnce(new SharedPlanError('SharedPlanConflict', 409)).mockResolvedValue(latest())
    await mount(); await click(en.common.edit); await typeName('Keep until explicit discard'); await click(en.action.save)
    expect(nameInput().value).toBe('Keep until explicit discard')
    await click(en.sharedPlan.loadServer)
    expect(nameInput().value).toBe('Competing server name'); expect(lineSharedApi.save).toHaveBeenCalledTimes(1)
    await click(en.action.save)
    expect(lineSharedApi.save).toHaveBeenLastCalledWith('plan', expect.objectContaining({ planName: 'Competing server name', lineId: 'line2', rounds: ['Night'], expectedRevision: 4 }))
  })
  it.each(['SharedPlanOverlap', 'DuplicateSharedPlanNo', 'SharedPlanUnadmitted'] as const)('does not treat %s as a stale revision', async code => {
    vi.mocked(lineSharedApi.save).mockRejectedValue(new SharedPlanError(code, 409))
    await mount(); await click(en.common.edit); await typeName('My draft'); await click(en.action.save)
    expect(lineSharedApi.get).not.toHaveBeenCalled(); expect(nameInput().value).toBe('My draft'); expect(host.textContent).toContain(en.sharedPlan[code])
  })
  it.each([[401, 'Unavailable', 'loginRequired'], [403, 'Unavailable', 'Forbidden'], [404, 'NotFound', 'planMissing'], [503, 'LineConfigurationUnavailable', 'LineConfigurationUnavailable'], [0, 'Unavailable', 'Unavailable']] as const)('preserves drafts and blocks writes after refresh failure%s, then allows a verified refresh (%s)', async (status, code, label) => {
    vi.mocked(lineSharedApi.save).mockRejectedValue(new SharedPlanError('SharedPlanConflict', 409))
    vi.mocked(lineSharedApi.get).mockRejectedValueOnce(new SharedPlanError(code, status)).mockResolvedValue(latest())
    await mount(); await click(en.common.edit); await typeName('Preserved through refresh failure'); await click(en.action.save)
    expect(nameInput().value).toBe('Preserved through refresh failure'); expect(button(en.action.save).disabled).toBe(true)
    expect(host.textContent).toContain(en.sharedPlan[label]); expect(lineSharedApi.save).toHaveBeenCalledTimes(1)
    await click(en.sharedPlan.refreshCurrent)
    expect(host.textContent).toContain('Competing server name'); expect(nameInput().value).toBe('Preserved through refresh failure')
    expect(button(en.action.save).disabled).toBe(true); expect(lineSharedApi.save).toHaveBeenCalledTimes(1)
  })
  it('does not overwrite edits typed while the authoritative GET is in flight', async () => {
    let finish!: (value: SharedPlan) => void
    vi.mocked(lineSharedApi.get).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    vi.mocked(lineSharedApi.save).mockRejectedValue(new SharedPlanError('SharedPlanConflict', 409))
    await mount(); await click(en.common.edit); await typeName('Before GET'); await click(en.action.save)
    await typeName('Typed during GET'); finish(latest()); await flush()
    expect(nameInput().value).toBe('Typed during GET'); expect(button(en.action.save).disabled).toBe(true)
  })
  it('does not allow acknowledgement of an unadmitted historical server version', async () => {
    vi.mocked(lineSharedApi.get).mockResolvedValue({ ...latest(), sharedPlanRevision: null, configurationStatus: 'HistoricalUnadmitted' })
    vi.mocked(lineSharedApi.save).mockRejectedValue(new SharedPlanError('SharedPlanConflict', 409))
    await mount(); await click(en.common.edit); await typeName('My draft'); await click(en.action.save)
    expect(button(en.sharedPlan.keepDraftAcknowledge.replace('{revision}', '')).disabled).toBe(true)
    expect(button(en.action.save).disabled).toBe(true); expect(nameInput().value).toBe('My draft')
  })
  it('refreshes the actual committed status after a toggle conflict and retries only on a new user action', async () => {
    vi.mocked(lineSharedApi.toggle).mockRejectedValueOnce(new SharedPlanError('SharedPlanConflict', 409)).mockResolvedValue({ ...latest(), isEnabled: false, sharedPlanRevision: 5 })
    await mount(); await click(en.plan.enable)
    expect(lineSharedApi.get).toHaveBeenCalledWith('plan'); expect(lineSharedApi.toggle).toHaveBeenCalledTimes(1)
    expect(button(en.plan.disable)).toBeDefined(); expect(host.querySelector('[data-testid=plans-table]')!.textContent).toContain(en.common.enabled)
    await click(en.plan.disable)
    expect(lineSharedApi.toggle).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'plan', isEnabled: true, sharedPlanRevision: 4 }))
  })
  it('keeps disable available when the line directory is unavailable', async () => {
    vi.mocked(lineSharedApi.list).mockResolvedValue({ items: [{ ...row, isEnabled: true }], total: 1 })
    vi.mocked(lineSharedApi.lines).mockRejectedValue(new SharedPlanError('LineConfigurationUnavailable', 503))
    await mount(); expect(button(en.plan.disable).disabled).toBe(false); expect(button(en.action.new).disabled).toBe(true)
    await click(en.plan.disable); expect(lineSharedApi.toggle).toHaveBeenCalledTimes(1)
  })
})
