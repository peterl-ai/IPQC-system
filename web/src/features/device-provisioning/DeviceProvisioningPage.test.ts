/* eslint-disable vue/one-component-per-file -- Test-local component stubs isolate UI behavior. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import DeviceProvisioningPage from './DeviceProvisioningPage.vue'
import { provisioningApi, ProvisioningApiError, type Audit, type Device, type Line, type Snapshot } from './api'
vi.mock('./api', async (original) => ({ ...await original<typeof import('./api')>(), provisioningApi: {
  lines: vi.fn(), devices: vi.fn(), lineOptions: vi.fn(), createLine: vi.fn(), renameLine: vi.fn(), setLineActive: vi.fn(),
  revokeDevice: vi.fn(), audit: vi.fn(),
} }))
const active: Line = { id: 'line', code: 'L1', name: 'Line', isActive: true, revision: 3 }
const inactive: Line = { ...active, id: 'inactive', code: 'L2', isActive: false }
const pending: Device = { id: 'pad', displayName: 'PAD A', model: null, serial: null, status: 'PendingCredential', lineId: 'line', revision: 7, bindingVersion: 4, replacedByDeviceId: null, createdAtUtc: '2026-11-06T12:00:00Z', updatedAtUtc: '2026-11-06T12:00:00Z', terminalAtUtc: null, terminalReason: null, credentialStatus: 'NotEnrolled', canExecuteLineShared: false }
let app: App, host: HTMLDivElement
const flush = async () => { for (let i = 0; i < 8; i++) { await Promise.resolve(); await nextTick() } }
const button = (text: string) => { const b = [...host.querySelectorAll('button')].find(x => x.textContent === text); expect(b, text).toBeTruthy(); return b! }
async function click(text: string) { button(text).click(); await flush() }
async function fill(id: string, value: string) { const input = host.querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`)!; input.value = value; input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true })); await flush() }
async function mount(locale = 'en') {
  host = document.createElement('div'); document.body.append(host); app = createApp(DeviceProvisioningPage)
  app.use(createI18n({ legacy: false, locale, messages: { en, 'zh-CN': zh } }))
  app.component('AButton', defineComponent({ inheritAttrs: false, setup(_, { attrs, slots }) { return () => h('button', attrs, slots.default?.()) } }))
  app.component('ASpace', defineComponent({ setup(_, { slots }) { return () => h('span', slots.default?.()) } }))
  app.component('AAlert', defineComponent({ props: { message: { type: String, default: '' } }, setup(p) { return () => h('div', { role: 'alert' }, p.message) } }))
  app.component('AModal', defineComponent({ props: { open: Boolean, okText: { type: String, default: '' } }, emits: ['ok', 'cancel'], setup(p, { slots, emit }) { return () => p.open ? h('section', { role: 'dialog' }, [slots.default?.(), h('button', { 'data-confirm': '', onClick: () => emit('ok') }, p.okText)]) : null } }))
  app.component('ADrawer', defineComponent({ props: { open: Boolean }, setup(p, { slots }) { return () => p.open ? h('aside', slots.default?.()) : null } }))
  app.mount(host); await flush()
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(provisioningApi.lines).mockResolvedValue({ items: [active, inactive], total: 2, page: 1, pageSize: 20 })
  vi.mocked(provisioningApi.devices).mockResolvedValue({ items: [pending], total: 1, page: 1, pageSize: 20 })
  vi.mocked(provisioningApi.lineOptions).mockResolvedValue([active, inactive])
})
afterEach(() => { app?.unmount(); host?.remove() })
describe('Retired provisioning historical view', () => {
  it.each(['en', 'zh-CN'])('preserves line create, rename, activate and deactivate (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    await mount(locale); await click(labels.provisioning.add); await click(labels.common.confirm)
    expect(provisioningApi.createLine).not.toHaveBeenCalled()
    await fill('provision-code', 'L3'); await fill('provision-name', 'Third'); await click(labels.common.confirm)
    expect(provisioningApi.createLine).toHaveBeenCalledWith('L3', 'Third')
    await click(labels.common.edit); await fill('provision-name', 'Rename'); await click(labels.common.confirm)
    expect(provisioningApi.renameLine).toHaveBeenCalledWith(active, 'Rename')
    await click(labels.provisioning.deactivate); await click(labels.common.confirm)
    expect(provisioningApi.setLineActive).toHaveBeenCalledWith(active, false)
    await click(labels.provisioning.activate); await click(labels.common.confirm)
    expect(provisioningApi.setLineActive).toHaveBeenCalledWith(inactive, true)
  })
  it.each(['en', 'zh-CN'])('removes retired device actions and keeps explicit reason-checked revoke (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    await mount(locale); await click(labels.provisioning.devices)
    const text = host.textContent ?? ''
    for (const label of [labels.provisioning.add, labels.common.edit, labels.provisioning.bind, labels.provisioning.rebind, labels.provisioning.unbind, labels.provisioning.replace]) expect([...host.querySelectorAll('button')].some(x => x.textContent === label)).toBe(false)
    expect(text).toContain(labels.provisioning.boundary)
    await click(labels.provisioning.revoke); await click(labels.common.confirm); expect(provisioningApi.revokeDevice).not.toHaveBeenCalled()
    await fill('provision-reason', 'Retire'); await click(labels.common.confirm)
    expect(provisioningApi.revokeDevice).toHaveBeenCalledWith(pending, 'Retire')
  })
  it('keeps keyboard tabs and line/device pagination independent', async () => {
    vi.mocked(provisioningApi.lines).mockResolvedValue({ items: [active], total: 40, page: 1, pageSize: 20 })
    vi.mocked(provisioningApi.devices).mockResolvedValue({ items: [pending], total: 40, page: 1, pageSize: 20 })
    await mount(); await click(en.provisioning.next)
    expect(provisioningApi.lines).toHaveBeenLastCalledWith(2)
    const first = button(en.provisioning.lines); first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })); await flush()
    expect(button(en.provisioning.devices).getAttribute('aria-selected')).toBe('true')
    await click(en.provisioning.next); expect(provisioningApi.devices).toHaveBeenLastCalledWith(2)
    await click(en.provisioning.lines); expect(host.textContent).toContain('2')
  })
  it.each(['en', 'zh-CN'])('preserves stable replacement identities, actor provenance and audit pagination (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    const snapshot: Snapshot = { name: 'Duplicate', revision: 2, code: null, isActive: null, model: null, serial: null, status: 'PendingCredential', lineId: active.id, bindingVersion: 2, replacedByDeviceId: null }
    const old: Audit = { id: 'old-event', changeId: 'shared-change', deviceId: 'old-device', action: 'DeviceReplaced', actorUserId: 'admin', actorUsername: 'admin', actorKind: 'Admin', occurredAtUtc: '2026-11-06T12:00:00Z', reason: 'Historical replacement', before: snapshot, after: { ...snapshot, status: 'Replaced', replacedByDeviceId: 'new-device' } }
    const next: Audit = { ...old, id: 'new-event', deviceId: 'new-device', action: 'CredentialRevocationRecorded', actorKind: 'System', actorUserId: null, actorUsername: 'EnrollmentIssuer', enrollmentId: 'original-enrollment', credentialId: 'original-credential' }
    vi.mocked(provisioningApi.audit).mockResolvedValueOnce({ items: [old], total: 2, page: 1, pageSize: 20 }).mockResolvedValueOnce({ items: [next], total: 2, page: 2, pageSize: 20 })
    await mount(locale); await click(labels.provisioning.audit); await click(labels.provisioning.more)
    expect(provisioningApi.audit).toHaveBeenLastCalledWith('production-lines', active.id, 2)
    const entries = [...host.querySelectorAll('aside article')]; expect(entries).toHaveLength(2)
    expect(entries[0]?.textContent).toContain('old-device'); expect(entries[1]?.textContent).toContain('new-device')
    expect(entries[1]?.textContent).toContain(labels.enrollment.actor.System)
    expect(entries[1]?.textContent).toContain('original-credential'); expect(entries[1]?.textContent).toContain('original-enrollment')
    for (const entry of entries) { expect(entry.textContent).toContain('shared-change'); expect(entry.textContent).toContain('Historical replacement'); expect(entry.textContent).toContain(labels.provisioning.before); expect(entry.textContent).toContain(labels.provisioning.after) }
    expect(host.textContent).not.toContain('provisioning.auditAction.'); expect(host.textContent).not.toContain('enrollment.actor.')
  })
  it('preserves stale conflict handling without automatic revoke retry', async () => {
    vi.mocked(provisioningApi.revokeDevice).mockRejectedValue(new ProvisioningApiError('Conflict', 409))
    await mount(); await click(en.provisioning.devices); await click(en.provisioning.revoke); await fill('provision-reason', 'Retire'); await click(en.common.confirm)
    expect(host.textContent).toContain(en.provisioning.error.Conflict); expect(provisioningApi.revokeDevice).toHaveBeenCalledTimes(1)
    expect(host.querySelector('[role=dialog]')).not.toBeNull()
  })
  it('keeps terminal devices historical and surfaces list failures', async () => {
    vi.mocked(provisioningApi.devices).mockResolvedValue({ items: [{ ...pending, status: 'Replaced' }], total: 1, page: 1, pageSize: 20 })
    await mount(); await click(en.provisioning.devices)
    expect([...host.querySelectorAll('button')].some(x => x.textContent === en.provisioning.revoke)).toBe(false)
    vi.mocked(provisioningApi.lines).mockRejectedValue(new Error('offline')); await click(en.provisioning.refresh)
    expect(host.textContent).toContain(en.provisioning.error.Unavailable)
  })
})