/* eslint-disable vue/one-component-per-file -- Test-local components. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import Panel from './DeviceEnrollmentPanel.vue'
import { ProvisioningApiError, type Device } from './api'
import { enrollmentApi, type Enrollment } from './enrollmentApi'
vi.mock('./enrollmentApi', () => ({ enrollmentApi: { list: vi.fn(), cancel: vi.fn() } }))
const device: Device = { id: 'device', displayName: 'Synthetic PAD', model: null, serial: null, status: 'PendingCredential', lineId: 'line', revision: 8, bindingVersion: 3, replacedByDeviceId: null, createdAtUtc: '', updatedAtUtc: '', terminalAtUtc: null, terminalReason: null, credentialStatus: 'NotEnrolled', canExecuteLineShared: false }
const pending: Enrollment = { id: 'request', deviceId: 'device', lineId: 'line', sourceBindingVersion: 3, targetBindingVersion: 4, environment: 'test-environment', revision: 3, state: 'PendingApproval', expiresAtUtc: '2026-11-09T20:00:00Z', csrSha256: 'A'.repeat(64), spkiSha256: 'B'.repeat(64), approvedByUserId: null, approvedAtUtc: null, terminalReason: null, candidate: null, canExecuteLineShared: false }
let app: App, host: HTMLDivElement
const flush = async () => { await new Promise(resolve => setTimeout(resolve, 0)); await nextTick() }
const button = (text: string) => [...host.querySelectorAll('button')].find(x => x.textContent === text)!
async function click(text: string) { button(text).click(); await flush() }
async function mount(locale = 'en', row = device) {
  host = document.createElement('div'); document.body.append(host)
  app = createApp(Panel, { device: row }); app.use(createI18n({ legacy: false, locale, messages: { en, 'zh-CN': zh } }))
  app.component('ADrawer', defineComponent({ setup: (_, { slots }) => () => h('aside', slots.default?.()) }))
  app.component('AAlert', defineComponent({ props: { message: { type: String, default: '' } }, setup: p => () => h('p', p.message) }))
  app.component('AButton', defineComponent({ inheritAttrs: false, setup: (_, { slots, attrs }) => () => h('button', attrs, slots.default?.()) }))
  app.mount(host); await flush()
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(enrollmentApi.list).mockResolvedValue({ items: [pending], total: 1, page: 1, pageSize: 20 })
  vi.mocked(enrollmentApi.cancel).mockResolvedValue({ ...pending, state: 'Cancelled' })
})
afterEach(() => { app?.unmount(); host?.remove() })
describe('Historical enrollment and controlled closure', () => {
  it.each(['en', 'zh-CN'])('shows history without invitation, approval, verification or secret controls (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    await mount(locale)
    expect(host.textContent).toContain(labels.enrollment.boundary)
    expect(button(labels.enrollment.invite)).toBeUndefined(); expect(button(labels.enrollment.approve)).toBeUndefined()
    expect(host.querySelector('input[type=checkbox]')).toBeNull(); expect(host.querySelector('.enrollment-secret')).toBeNull()
    expect(host.textContent).toContain(pending.id); expect(host.textContent).toContain(pending.spkiSha256)
    expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0)
  })
  it.each(['en', 'zh-CN'])('permits reason/revision-checked closure of an Expired projection (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    vi.mocked(enrollmentApi.list).mockResolvedValue({ items: [{ ...pending, state: 'Expired' }], total: 1, page: 1, pageSize: 20 })
    await mount(locale); expect(button(labels.enrollment.cancel).disabled).toBe(true)
    const reason = host.querySelector('textarea')!; reason.value = 'Close old request'; reason.dispatchEvent(new Event('input')); await flush()
    await click(labels.enrollment.cancel)
    expect(enrollmentApi.cancel).toHaveBeenCalledWith(pending.id, pending.revision, 'Close old request')
  })
  it('shows stale cancellation conflicts without automatic mutation retries', async () => {
    vi.mocked(enrollmentApi.cancel).mockRejectedValue(new ProvisioningApiError('Conflict', 409))
    await mount(); const reason = host.querySelector('textarea')!; reason.value = 'Close'; reason.dispatchEvent(new Event('input')); await flush()
    await click(en.enrollment.cancel); expect(enrollmentApi.cancel).toHaveBeenCalledTimes(1)
    expect(host.textContent).toContain(en.provisioning.error.Conflict)
  })
  it.each(['en', 'zh-CN'])('retains candidate IDs, original operation, pending cleanup and paginated terminal history (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    const issued: Enrollment = { ...pending, state: 'IssuedUnconfirmed', candidate: { id: 'original-credential', issuanceOperationId: 'original-operation', state: 'Revoked', bindingVersion: 4, certificateSha256: 'C'.repeat(64), certificatePem: null, rootCertificatePem: '', notBeforeUtc: pending.expiresAtUtc, notAfterUtc: pending.expiresAtUtc, externalRevocationPending: true, lastErrorCode: 'IssuerUnavailable', canExecuteLineShared: false } }
    vi.mocked(enrollmentApi.list).mockResolvedValueOnce({ items: [issued], total: 2, page: 1, pageSize: 20 }).mockResolvedValueOnce({ items: [{ ...pending, id: 'previous', state: 'Cancelled' }], total: 2, page: 2, pageSize: 20 })
    await mount(locale); expect(host.textContent).toContain('original-operation'); expect(host.textContent).toContain('original-credential')
    expect(host.textContent).toContain(labels.enrollment.revocationPending); expect(host.textContent).toContain(labels.enrollment.issuerRecovery)
    await click(labels.provisioning.more); expect(enrollmentApi.list).toHaveBeenLastCalledWith(device.id, 2)
    expect(host.querySelectorAll('article')).toHaveLength(2); expect(host.querySelectorAll('textarea')).toHaveLength(1)
    expect(host.textContent).not.toContain('mTLS'); expect(host.textContent).not.toContain('enrollment.state.')
  })
  it('keeps replaced-device enrollment history visible', async () => {
    await mount('en', { ...device, status: 'Replaced', lineId: null })
    expect(host.querySelectorAll('article')).toHaveLength(1); expect(button(en.enrollment.invite)).toBeUndefined()
  })
})