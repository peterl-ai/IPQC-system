/* eslint-disable vue/one-component-per-file -- Test-local UI adapters. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App, type PropType } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import Panel from './LineSharedPlansPanel.vue'
import { lineSharedApi, SharedPlanError, type SharedPlan } from './lineSharedApi'
import { patrolPlansApi } from './api'
vi.mock('./lineSharedApi', async original => ({ ...await original<typeof import('./lineSharedApi')>(), lineSharedApi: { lines: vi.fn(), list: vi.fn(), save: vi.fn(), toggle: vi.fn() } }))
vi.mock('./api', () => ({ patrolPlansApi: { standards: vi.fn() } }))
let app: App, host: HTMLDivElement
const flush = async () => { await new Promise(resolve => setTimeout(resolve, 0)); await nextTick() }
const row: SharedPlan = { id: 'plan', planNo: 'Synthetic', planName: 'Original', patrolStandardId: 's', lineId: 'line', rounds: ['Day'], effectiveStartLocal: '2026-11-10T07:00', effectiveEndLocal: null, isEnabled: false, sharedPlanRevision: 3, configurationStatus: 'Admitted', generationNotBeforeUtc: '' }
const button = (label: string) => [...host.querySelectorAll('button')].find(x => x.textContent === label)!
async function mount(locale: string) {
  host = document.createElement('div'); document.body.append(host)
  app = createApp(Panel); app.use(createI18n({ legacy: false, locale, messages: { en, 'zh-CN': zh } }))
  const container = defineComponent({ setup: (_, { slots }) => () => h('div', [slots.default?.(), slots.footer?.()]) })
  for (const name of ['ASpace', 'AForm', 'AFormItem', 'ADrawer', 'ACheckboxGroup', 'ASelect', 'ASwitch']) app.component(name, container)
  app.component('AAlert', defineComponent({ props: { message: { type: String, default: '' } }, setup: p => () => h('p', p.message) }))
  app.component('AButton', defineComponent({ setup: (_, { slots, attrs }) => () => h('button', attrs, slots.default?.()) }))
  app.component('AInput', defineComponent({ props: { value: { type: String, default: '' } }, emits: ['update:value'], setup: (p, { emit }) => () => h('input', { value: p.value, onInput: (e: Event) => emit('update:value', (e.target as HTMLInputElement).value) }) }))
  app.component('ATable', defineComponent({ props: { dataSource: { type: Array as PropType<SharedPlan[]>, default: () => [] } }, setup: (p, { slots }) => () => h('div', p.dataSource?.map(record => slots.bodyCell?.({ column: { key: 'actions' }, record }))) }))
  app.mount(host); await flush()
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(lineSharedApi.lines).mockResolvedValue([{ id: 'line', code: 'L1', name: 'Synthetic 1', selectionOrder: 1 }, { id: 'line2', code: 'L2', name: 'Synthetic 2', selectionOrder: 2 }])
  vi.mocked(lineSharedApi.list).mockResolvedValue({ items: [row], total: 1 }); vi.mocked(patrolPlansApi.standards).mockResolvedValue([])
})
afterEach(() => { app?.unmount(); host?.remove() })
describe('R03-34 shared plan management panel', () => {
  it.each(['en', 'zh-CN'])('preserves unsaved input and revision after conflict (%s)', async locale => {
    const labels = locale === 'en' ? en : zh; await mount(locale)
    expect(host.textContent).toContain(labels.sharedPlan.notice)
    button(labels.common.edit).click(); await flush()
    const input = host.querySelectorAll('input')[1]!; input.value = 'Unsaved contribution'; input.dispatchEvent(new Event('input')); await flush()
    vi.mocked(lineSharedApi.save).mockRejectedValue(new SharedPlanError('SharedPlanConflict', 409))
    button(labels.action.save).click(); await flush()
    expect(lineSharedApi.save).toHaveBeenCalledTimes(1)
    expect(lineSharedApi.save).toHaveBeenCalledWith('plan', expect.objectContaining({ planName: 'Unsaved contribution', expectedRevision: 3 }))
    expect(host.textContent).toContain(labels.sharedPlan.conflict); expect(input.value).toBe('Unsaved contribution')
    expect(host.textContent).not.toContain('sharedPlan.'); expect(localStorage.length).toBe(0)
  })
  it('keeps disable available when the line directory is unavailable', async () => {
    vi.mocked(lineSharedApi.list).mockResolvedValue({ items: [{ ...row, isEnabled: true }], total: 1 })
    vi.mocked(lineSharedApi.lines).mockRejectedValue(new SharedPlanError('LineConfigurationUnavailable', 503))
    await mount('en'); expect(button(en.plan.disable).disabled).toBe(false); expect(button(en.action.new).disabled).toBe(true)
    button(en.plan.disable).click(); await flush(); expect(lineSharedApi.toggle).toHaveBeenCalledTimes(1)
  })
})
