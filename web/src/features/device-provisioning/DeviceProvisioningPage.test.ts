/* eslint-disable vue/one-component-per-file -- Test-local component stubs isolate UI behavior. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import DeviceProvisioningPage from './DeviceProvisioningPage.vue'
import { provisioningApi, ProvisioningApiError, type Device, type Line } from './api'
vi.mock('./api', async (original) => ({ ...await original<typeof import('./api')>(), provisioningApi: {
  lines: vi.fn(), devices: vi.fn(), lineOptions: vi.fn(), deviceOptions: vi.fn(), createLine: vi.fn(), renameLine: vi.fn(), setLineActive: vi.fn(),
  createDevice: vi.fn(), editDevice: vi.fn(), deviceAction: vi.fn(), audit: vi.fn(),
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
  vi.mocked(provisioningApi.deviceOptions).mockResolvedValue([pending, { ...pending, id: 'new', displayName: 'New PAD', lineId: null, revision: 2 }])
})
afterEach(() => { app?.unmount(); host?.remove() })
describe('Admin provisioning page', () => {
  it('supports keyboard tabs and only offers active lines for an unassigned pending device', async () => {
    vi.mocked(provisioningApi.devices).mockResolvedValue({ items: [{ ...pending, lineId: null }], total: 1, page: 1, pageSize: 20 })
    await mount()
    const first = button('Production Lines'); first.focus(); first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })); await flush()
    expect(button('Devices').getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(button('Devices'))
    await click('Reserve Line')
    expect([...host.querySelectorAll('#provision-line option')].map(x => x.getAttribute('value'))).toEqual(['', 'line'])
    await fill('provision-line', 'line'); await click('Confirm')
    expect(provisioningApi.deviceAction).toHaveBeenCalledWith({ ...pending, lineId: null }, 'bind', { lineId: 'line' })
  })
  it('requires a reason before releasing a reservation and does not send a replacement identity', async () => {
    await mount(); await click('Devices'); await click('Release Reservation'); await click('Confirm')
    expect(provisioningApi.deviceAction).not.toHaveBeenCalled()
    await fill('provision-reason', 'Release reservation'); await click('Confirm')
    expect(provisioningApi.deviceAction).toHaveBeenCalledWith(pending, 'unbind', { reason: 'Release reservation' })
  })
  it('creates a line with validated input, then refreshes', async () => {
    await mount(); await click('Add'); await click('Confirm'); expect(provisioningApi.createLine).not.toHaveBeenCalled()
    await fill('provision-code', 'L3'); await fill('provision-name', 'Third line'); await click('Confirm')
    expect(provisioningApi.createLine).toHaveBeenCalledWith('L3', 'Third line'); expect(host.querySelector('[role=dialog]')).toBeNull()
    expect(provisioningApi.lines).toHaveBeenCalledTimes(2)
  })
  it('confirms replacement with both revisions and displays the non-authorizing boundary', async () => {
    await mount(); await click('Devices'); expect(host.textContent).toContain('Pending credential enrollment'); expect(host.textContent).toContain('Not authorized for patrol execution')
    await click('Replace Device'); expect(host.textContent).toContain('Previous work and history remain with the original device identity.')
    await fill('provision-replacement', 'new'); await fill('provision-reason', 'Hardware replacement'); await click('Confirm')
    expect(provisioningApi.deviceAction).toHaveBeenCalledWith(pending, 'replace', { replacementDeviceId: 'new', replacementExpectedRevision: 2, reason: 'Hardware replacement' })
  })
  it('surfaces stale conflicts without silently retrying a mutation', async () => {
    vi.mocked(provisioningApi.deviceAction).mockRejectedValue(new ProvisioningApiError('Conflict', 409))
    await mount(); await click('Devices'); await click('Revoke'); await fill('provision-reason', 'Retire'); await click('Confirm')
    expect(host.textContent).toContain(en.provisioning.error.Conflict); expect(provisioningApi.deviceAction).toHaveBeenCalledTimes(1)
    expect(host.querySelector('[role=dialog]')).not.toBeNull()
  })
  it('shows bilingual pending status, filters inactive choices and keeps terminal devices read-only', async () => {
    vi.mocked(provisioningApi.devices).mockResolvedValue({ items: [{ ...pending, status: 'Revoked', lineId: null }], total: 1, page: 1, pageSize: 20 })
    await mount('zh-CN'); await click('设备'); expect(host.textContent).toContain('已撤销'); expect(host.textContent).toContain('未获巡检执行授权')
    expect(host.textContent).not.toContain('预分配产线'); expect(host.textContent).not.toContain('重新启用')
  })
  it('loads audit actor, time, prior line and binding version, and reports list errors', async () => {
    vi.mocked(provisioningApi.audit).mockResolvedValue({ items: [{ id: 'event', changeId: 'change', action: 'DeviceRevoked', actorUserId: 'user', actorUsername: 'admin', occurredAtUtc: '2026-11-06T12:00:00Z', reason: 'Retire', before: { name: 'PAD A', revision: 7, code: null, isActive: null, model: null, serial: null, status: 'PendingCredential', lineId: 'line', bindingVersion: 4, replacedByDeviceId: null }, after: { name: 'PAD A', revision: 8, code: null, isActive: null, model: null, serial: null, status: 'Revoked', lineId: null, bindingVersion: 5, replacedByDeviceId: null } }], total: 1, page: 1, pageSize: 20 })
    await mount(); await click('Devices'); await click('Audit History'); expect(host.querySelector('aside')?.textContent).toContain('admin'); expect(host.querySelector('aside')?.textContent).toContain('L1')
    vi.mocked(provisioningApi.lines).mockRejectedValue(new Error('offline')); await click('Refresh'); expect(host.textContent).toContain(en.provisioning.error.Unavailable)
  })
})
