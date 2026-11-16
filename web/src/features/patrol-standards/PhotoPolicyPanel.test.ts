/* eslint-disable vue/one-component-per-file -- Small test-local Ant adapters; production logic and native controls are real. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App, type PropType } from 'vue'
import { createI18n } from 'vue-i18n'
import { authSession, type CurrentUser } from '@/services/authSession'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import Panel from './PhotoPolicyPanel.vue'
import Standards from './PatrolStandardsPage.vue'
import { ApiError, patrolStandardsApi } from './api'
import { emptyPatrolStandardItem } from './model'
import { intentKey, type PhotoPolicyInput, type PhotoPolicyPage, type PhotoPolicyReceipt } from './photoPolicyModel'

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), patrolStandardsApi: {
  photoPolicies: vi.fn(), confirmPhotoPolicy: vi.fn(), photoPolicyReceipt: vi.fn(), list: vi.fn(), get: vi.fn(), update: vi.fn(), create: vi.fn(),
} }))
const ids = { standard: '00000000-0000-4000-8000-000000000001', item: '00000000-0000-4000-8000-000000000002',
  actor: '00000000-0000-4000-8000-000000000003', definition: '00000000-0000-4000-8000-000000000004',
  confirmation: '00000000-0000-4000-8000-000000000005', family: '00000000-0000-4000-8000-000000000006' }
const source = () => ({ id: ids.standard, name: 'Synthetic exact Standard', factoryCode: 'F', factoryName: 'Synthetic factory', workshopCode: 'W',
  lineCode: 'Legacy', lineName: 'Legacy line', materialCode: 'M', createdBy: 'synthetic', createdTime: '', updatedBy: 'synthetic', updatedTime: '',
  inspectionItems: [{ ...emptyPatrolStandardItem(ids.item), inspectionItem: 'Synthetic exact item', inspectionContent: 'Actual content', sampleCount: '5', photoRequirement: 'Required' }] })
const fresh = (): PhotoPolicyPage => ({ standardId: ids.standard, reviewStandard: source(), items: [{ standardId: ids.standard, standardItemId: ids.item,
  rawPhotoRequirement: 'Required', definitionId: null, definitionVersion: null, definitionHash: 'A'.repeat(64), state: 'Unconfirmed', confirmation: null }] })
let app: App, host: HTMLDivElement, server: PhotoPolicyPage, receipts: Map<string, PhotoPolicyReceipt>
const flush = async () => { await new Promise(resolve => setTimeout(resolve, 0)); await nextTick() }
const buttons = (text: string) => [...host.querySelectorAll('button')].filter(x => x.textContent === text)
const button = (text: string) => buttons(text)[0]!
async function click(text: string) { button(text).click(); await flush() }
async function select(value: string) { const el = host.querySelector('select')!; el.value = value; el.dispatchEvent(new Event('change')); await flush() }
async function review() { const el = host.querySelector<HTMLInputElement>('input[type=checkbox]')!; el.click(); await flush() }
function actor(role: CurrentUser['role'] = 'PQE') {
  authSession.user.value = { id: ids.actor, username: 'synthetic', displayName: 'Synthetic reviewer', role, isActive: true, mustChangePassword: false,
    permissions: role === 'IPQA' ? ['CompletedRecords.Read'] : ['PatrolStandards.Read', 'PatrolStandards.Write', ...(role === 'PQE' ? ['PhotoPolicies.Confirm'] : [])] }
}
function receipt(input: PhotoPolicyInput): PhotoPolicyReceipt { return { confirmationId: ids.confirmation, requestId: input.requestId, standardId: ids.standard,
  standardItemId: ids.item, definitionId: input.expectedDefinitionId ?? ids.definition, definitionVersion: 1, canonicalizationVersion: 1,
  definitionHash: input.expectedDefinitionHash, policyVersion: 1, value: input.value, confirmedByUserId: ids.actor, confirmedPersonalSessionId: ids.family,
  environmentId: 'synthetic-test', confirmedAtUtc: '2026-11-16T16:00:00Z' } }
function commit(input: PhotoPolicyInput) {
  const r = receipt(input); receipts.set(r.confirmationId, r)
  server.items[0] = { ...server.items[0]!, state: 'ExplicitlyConfirmed', definitionId: r.definitionId, definitionVersion: r.definitionVersion, confirmation: r }
  return r
}
function unmount() { app?.unmount(); host?.remove() }
async function mount(locale = 'en', root: 'panel' | 'standards' = 'panel') {
  host = document.createElement('div'); document.body.append(host)
  app = root === 'panel' ? createApp(Panel, { standardId: ids.standard, open: true, refreshVersion: 0 }) : createApp(Standards)
  app.use(createI18n({ legacy: false, locale, messages: { en, 'zh-CN': zh } }))
  const container = defineComponent({ setup: (_, { slots, expose }) => { expose({ validate: async () => {}, clearValidate: () => {} }); return () => h('div', [slots.default?.(), slots.footer?.()]) } })
  for (const name of ['ASpace', 'AForm', 'AFormItem', 'AUpload', 'APopconfirm']) app.component(name, container)
  for (const name of ['ADrawer', 'AModal']) app.component(name, defineComponent({ props: { open: Boolean }, setup: (p, { slots }) => () => p.open ? h('aside', [slots.default?.(), slots.footer?.()]) : null }))
  app.component('AAlert', defineComponent({ props: { message: { type: String, default: '' } }, setup: (p, { attrs }) => () => h('p', attrs, p.message) }))
  app.component('AEmpty', defineComponent({ props: { description: { type: String, default: '' } }, setup: p => () => h('p', p.description) }))
  app.component('AButton', defineComponent({ setup: (_, { slots, attrs }) => () => h('button', attrs, slots.default?.()) }))
  app.component('AInput', defineComponent({ props: { value: { type: String, default: '' } }, emits: ['update:value'], setup: (p, { emit }) => () => h('input', { value: p.value, onInput: (e: Event) => emit('update:value', (e.target as HTMLInputElement).value) }) }))
  app.component('ATable', defineComponent({ props: { dataSource: { type: Array as PropType<{ id: string; name: string }[]>, default: () => [] } },
    setup: (p, { slots }) => () => h('div', p.dataSource.map((record, index) => h('div', [record.name, slots.bodyCell?.({ column: { key: 'actions' }, record, index })]))) }))
  app.mount(host); await flush()
}
beforeEach(() => {
  vi.resetAllMocks(); sessionStorage.clear(); server = fresh(); receipts = new Map(); actor()
  authSession.initialized.value = true; authSession.accessToken.value = 'synthetic-test-only'
  vi.mocked(patrolStandardsApi.photoPolicies).mockImplementation(async () => structuredClone(server))
  vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockImplementation(async (_s, _i, input) => commit(input))
  vi.mocked(patrolStandardsApi.photoPolicyReceipt).mockImplementation(async id => structuredClone(receipts.get(id)!))
  vi.mocked(patrolStandardsApi.list).mockResolvedValue({ items: [source()], total: 1, page: 1, pageSize: 10 })
  vi.mocked(patrolStandardsApi.get).mockImplementation(async () => source())
  vi.mocked(patrolStandardsApi.update).mockImplementation(async () => source())
})
afterEach(() => { unmount(); sessionStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals(); authSession.clear() })

describe('Explicit photo-policy UI and actual saved request protocol', () => {
  it.each(['Required', 'Optional'] as const)('requires deliberate choice + review, then confirms %s with exact nullable preconditions', async value => {
    await mount(); expect(host.textContent).toContain('Required'); expect(host.textContent).toContain(en.photoPolicy.Unconfirmed)
    expect(host.querySelector('select')!.value).toBe(''); expect(button(en.photoPolicy.confirm).disabled).toBe(true)
    await select(value); expect(button(en.photoPolicy.confirm).disabled).toBe(true); await review(); await click(en.photoPolicy.confirm)
    const [standard, item, sent] = vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mock.calls[0]!
    expect([standard, item]).toEqual([ids.standard, ids.item]); expect(sent).toEqual({ requestId: expect.any(String), canonicalizationVersion: 1,
      expectedDefinitionId: null, expectedDefinitionHash: 'A'.repeat(64), expectedConfirmationId: null, value })
    expect(host.textContent).toContain(en.photoPolicy.accepted); expect(host.textContent).toContain(en.photoPolicy.ExplicitlyConfirmed)
    expect(host.textContent).toContain(ids.actor); expect(host.textContent).toContain(ids.confirmation)
    expect(sessionStorage.length).toBe(0); expect(patrolStandardsApi.create).not.toHaveBeenCalled()
    await click(en.photoPolicy.recoverReceipt); expect(patrolStandardsApi.photoPolicyReceipt).toHaveBeenCalledWith(ids.confirmation)
  })
  it('Admin can edit Standard but cannot confirm, even with a forged UI permission', async () => {
    actor('Admin'); authSession.user.value!.permissions.push('PhotoPolicies.Confirm'); await mount('en', 'standards')
    expect(button(en.common.edit)).toBeDefined(); await click(en.photoPolicy.title)
    expect(host.textContent).toContain(en.photoPolicy.viewOnly); expect(host.querySelector('select')).toBeNull()
    expect(button(en.photoPolicy.confirm)).toBeUndefined(); expect(patrolStandardsApi.confirmPhotoPolicy).not.toHaveBeenCalled()
  })
  it('IPQA has no Standard confirmation read or control, even if mounted directly', async () => {
    actor('IPQA'); await mount(); expect(host.textContent).toContain(en.photoPolicy.noAccess)
    expect(patrolStandardsApi.photoPolicies).not.toHaveBeenCalled(); expect(button(en.photoPolicy.confirm)).toBeUndefined()
  })
  it.each(['Unconfirmed', 'InvalidConfiguration', 'FutureConfiguration'])('shows %s without Optional default or an approval claim', async state => {
    server.items[0]!.state = state; server.items[0]!.definitionVersion = 2; server.items[0]!.definitionId = ids.definition; await mount()
    expect(host.querySelector('select')!.value).toBe(''); expect(host.textContent).not.toContain(en.photoPolicy.accepted)
    expect(host.textContent).toContain(state === 'Unconfirmed' ? en.photoPolicy.needsReconfirmation : en.photoPolicy.InvalidConfiguration)
    if (state === 'FutureConfiguration') { await select('Optional'); await review(); expect(button(en.photoPolicy.confirm).disabled).toBe(true) }
  })
  it('persists an unknown original request across remount and explicitly retries unchanged even after current policy changes', async () => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockImplementationOnce(async (_s, _i, input) => { commit(input); throw new ApiError('response lost', 0) })
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    const original = vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mock.calls[0]![2]; const originalReceipt = receipt(original)
    expect(host.textContent).toContain(en.photoPolicy.unknown); expect(host.textContent).not.toContain(en.photoPolicy.accepted)
    const saved = sessionStorage.getItem(intentKey(ids.actor, ids.standard))!
    expect(JSON.parse(saved).input).toEqual(original)
    expect(Object.keys(JSON.parse(saved)).sort()).toEqual(['actorId', 'input', 'itemId', 'standardId'])
    expect(saved).not.toContain('synthetic-test-only'); expect(saved).not.toMatch(/password|proof|accessToken|refreshToken|personalSessionId/i)
    // Another PQE superseded the accepted decision; latest status alone cannot recover this original.
    server.items[0]!.confirmation = { ...originalReceipt, requestId: crypto.randomUUID(), value: 'Optional', policyVersion: 2 }
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockResolvedValue(originalReceipt)
    unmount(); await mount(); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
    await click(en.photoPolicy.retry); expect(vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mock.calls[1]![2]).toEqual(original)
    expect(host.textContent).toContain(en.photoPolicy.accepted); expect(host.textContent).toContain(en.photoPolicy.Optional)
    expect(server.items[0]!.confirmation!.policyVersion).toBe(2); expect(sessionStorage.length).toBe(0)
  })
  it('recovers an exact accepted receipt on explicit refresh after a lost response without another POST', async () => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockImplementationOnce(async (_s, _i, input) => { commit(input); throw new ApiError('lost', 503) })
    await mount(); await select('Optional'); await review(); await click(en.photoPolicy.confirm)
    await click(en.photoPolicy.refresh)
    expect(patrolStandardsApi.photoPolicyReceipt).toHaveBeenCalledWith(ids.confirmation)
    expect(host.textContent).toContain(en.photoPolicy.accepted); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
  })
  it.each(['definition', 'competing'])('retains selection on %s conflict, refreshes, requires a new deliberate review and uses a new RequestId', async kind => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockImplementationOnce(async (_s, _i, input) => {
      if (kind === 'definition') { server.reviewStandard!.inspectionItems[0]!.inspectionContent = 'New exact content'; server.items[0]!.definitionHash = 'B'.repeat(64); server.items[0]!.definitionId = ids.definition; server.items[0]!.definitionVersion = 2 }
      else commit({ ...input, requestId: crypto.randomUUID(), value: 'Optional' })
      throw new ApiError('conflict', 409)
    })
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    const original = vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mock.calls[0]![2]
    expect(host.textContent).toContain(en.photoPolicy.conflict); expect(host.querySelector('select')!.value).toBe('Required')
    expect(button(en.photoPolicy.confirm).disabled).toBe(true); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
    if (kind === 'definition') expect(host.textContent).toContain('New exact content')
    await review(); await click(en.photoPolicy.confirm)
    const second = vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mock.calls[1]![2]
    expect(second.requestId).not.toBe(original.requestId); expect(second.value).toBe('Required')
    expect(second.expectedDefinitionHash).toBe(kind === 'definition' ? 'B'.repeat(64) : 'A'.repeat(64))
    expect(second.expectedConfirmationId).toBe(kind === 'competing' ? ids.confirmation : null)
  })
  it.each([400, 401, 403, 404, 409, 429])('reports HTTP%s without false success or automatic re-POST', async status => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockRejectedValue(new ApiError('synthetic rejection', status))
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    expect(host.textContent).toContain(en.photoPolicy[`error${status}` as keyof typeof en.photoPolicy])
    expect(host.textContent).not.toContain(en.photoPolicy.accepted); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
  })
  it('does not mix a different Standard or incoherent review text with a server hash', async () => {
    server.reviewStandard!.inspectionItems[0]!.photoRequirement = 'different'; await mount()
    expect(host.textContent).toContain(en.photoPolicy.unavailable); expect(button(en.photoPolicy.confirm)).toBeUndefined()
  })
  it('does not accept a mismatched receipt and retains the exact unknown request', async () => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockImplementation(async (_s, _i, input) => ({ ...receipt(input), standardItemId: crypto.randomUUID() }))
    await mount(); await select('Optional'); await review(); await click(en.photoPolicy.confirm)
    expect(host.textContent).not.toContain(en.photoPolicy.accepted); expect(host.textContent).toContain(en.photoPolicy.unknown)
    expect(sessionStorage.length).toBe(1)
  })
  it('never presents an older accepted request as success for a new unknown confirmation', async () => {
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    expect(host.textContent).toContain(en.photoPolicy.accepted)
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockRejectedValueOnce(new ApiError('new response lost', 0))
    await select('Optional'); await review(); await click(en.photoPolicy.confirm)
    expect(host.textContent).toContain(en.photoPolicy.unknown)
    expect(host.textContent).not.toContain(en.photoPolicy.accepted)
    expect(server.items[0]!.confirmation!.value).toBe('Required')
  })
  it('retains typed choice while conflict refresh is delayed and never resends from that response', async () => {
    await mount(); let finish!: (value: PhotoPolicyPage) => void
    vi.mocked(patrolStandardsApi.photoPolicies).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockRejectedValueOnce(new ApiError('stale', 409))
    await select('Optional'); await review(); await click(en.photoPolicy.confirm)
    finish(fresh()); await flush(); expect(host.querySelector('select')!.value).toBe('Optional')
    expect(button(en.photoPolicy.confirm).disabled).toBe(true); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
  })
  it('blocks POST when durable request retention fails', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('full') })
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    expect(host.textContent).toContain(en.photoPolicy.storageError); expect(patrolStandardsApi.confirmPhotoPolicy).not.toHaveBeenCalled()
  })
  it('does not transmit an old actor request after another user signs in', async () => {
    vi.mocked(patrolStandardsApi.confirmPhotoPolicy).mockRejectedValue(new ApiError('offline', 0))
    await mount(); await select('Required'); await review(); await click(en.photoPolicy.confirm)
    actor(); authSession.user.value!.id = '00000000-0000-4000-8000-000000000009'; await flush()
    expect(button(en.photoPolicy.retry)).toBeUndefined(); expect(patrolStandardsApi.confirmPhotoPolicy).toHaveBeenCalledTimes(1)
    expect(sessionStorage.getItem(intentKey(ids.actor, ids.standard))).toBeTruthy()
  })
  it.each(['en', 'zh-CN'])('renders bilingual review and limits without task readiness or Plan controls (%s)', async locale => {
    const labels = locale === 'en' ? en : zh; await mount(locale)
    expect(host.textContent).toContain(labels.photoPolicy.admissionWarning); expect(host.textContent).toContain(labels.photoPolicy.boundary)
    expect(host.querySelector('select')!.options[1]!.text).toBe(labels.photoPolicy.Required)
    expect(host.querySelector('select')!.options[2]!.text).toBe(labels.photoPolicy.Optional)
    expect(host.textContent).not.toContain('photoPolicy.'); expect(button(labels.plan.enable)).toBeUndefined()
    expect([...host.querySelectorAll('button')].some(x => /^(Submit|提交)$/.test(x.textContent ?? ''))).toBe(false)
    expect(Object.keys(en.photoPolicy)).toEqual(Object.keys(zh.photoPolicy))
  })
  it.each([false, true])('Standard Save never confirms; returning to policy reads actual invalidated/no-op status (changed=%s)', async changed => {
    commit({ requestId: crypto.randomUUID(), canonicalizationVersion: 1, expectedDefinitionId: null, expectedDefinitionHash: 'A'.repeat(64), expectedConfirmationId: null, value: 'Required' })
    vi.mocked(patrolStandardsApi.update).mockImplementation(async () => {
      if (changed) { server.items[0] = { ...server.items[0]!, state: 'Unconfirmed', confirmation: null, definitionVersion: 2, definitionHash: 'B'.repeat(64) } }
      return source()
    })
    await mount('en', 'standards'); await click(en.photoPolicy.title); expect(host.textContent).toContain(en.photoPolicy.ExplicitlyConfirmed)
    // The policy drawer's edit action leads to the existing Standard editor.
    const edits = buttons(en.common.edit); edits[edits.length - 1]!.click(); await flush()
    buttons(en.action.save).at(-1)!.click(); await flush(); await flush()
    expect(patrolStandardsApi.update).toHaveBeenCalledTimes(1); expect(patrolStandardsApi.confirmPhotoPolicy).not.toHaveBeenCalled()
    expect(patrolStandardsApi.photoPolicies).toHaveBeenCalledTimes(2)
    expect(host.textContent).toContain(changed ? en.photoPolicy.needsReconfirmation : en.photoPolicy.ExplicitlyConfirmed)
  })
})
