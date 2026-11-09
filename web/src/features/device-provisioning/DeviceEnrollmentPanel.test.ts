/* eslint-disable vue/one-component-per-file -- Test-local components. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import zh from '@/i18n/locales/zh-CN'
import Panel from './DeviceEnrollmentPanel.vue'
import { ProvisioningApiError, type Device } from './api'
import { enrollmentApi, type Enrollment } from './enrollmentApi'
vi.mock('./enrollmentApi', () => ({ enrollmentApi: { list: vi.fn(), invite: vi.fn(), approve: vi.fn(), cancel: vi.fn() } }))
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
  vi.mocked(enrollmentApi.approve).mockResolvedValue({ ...pending, state: 'Approved' })
  vi.mocked(enrollmentApi.cancel).mockResolvedValue({ ...pending, state: 'Cancelled' })
})
afterEach(() => { app?.unmount(); host?.remove() })
describe('Enrollment Admin boundary', () => {
  it.each(['en', 'zh-CN'])('requires physical verification and sends exact identity / fingerprints (%s)', async locale => {
    const labels = locale === 'en' ? en : zh
    await mount(locale)
    expect(host.textContent).toContain(labels.enrollment.boundary)
    expect(button(labels.enrollment.approve).disabled).toBe(true)
    const check = host.querySelector<HTMLInputElement>('input[type=checkbox]')!; check.checked = true; check.dispatchEvent(new Event('change')); await flush()
    await click(labels.enrollment.approve)
    expect(enrollmentApi.approve).toHaveBeenCalledWith(pending.id, { expectedRevision: 3, expectedDeviceRevision: 8, lineId: 'line', bindingVersion: 3, csrSha256: pending.csrSha256, spkiSha256: pending.spkiSha256, operationId: expect.any(String) })
  })
  it('keeps the same issuance approval ID on uncertain HTTP retry and never retries automatically', async () => {
    vi.mocked(enrollmentApi.approve).mockRejectedValue(new ProvisioningApiError('Conflict', 409))
    await mount(); const check = host.querySelector<HTMLInputElement>('input')!; check.checked = true; check.dispatchEvent(new Event('change')); await flush()
    await click(en.enrollment.approve)
    expect(enrollmentApi.approve).toHaveBeenCalledTimes(1); expect(host.textContent).toContain(en.provisioning.error.Conflict)
    await click(en.enrollment.approve)
    expect(vi.mocked(enrollmentApi.approve).mock.calls[0]).toEqual(vi.mocked(enrollmentApi.approve).mock.calls[1])
  })
  it('shows invitation secret once, never persists it, and clears it when dismissed or unmounted', async () => {
    vi.mocked(enrollmentApi.list).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 })
    vi.mocked(enrollmentApi.invite).mockResolvedValue({ enrollment: { ...pending, state: 'Invited' }, invitationSecret: 'synthetic-one-time-secret', invitationExpiresAtUtc: pending.expiresAtUtc })
    await mount(); await click(en.enrollment.invite)
    expect(enrollmentApi.invite).toHaveBeenCalledWith(device); expect(host.textContent).toContain('synthetic-one-time-secret')
    expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0)
    await click(en.enrollment.dismissSecret); expect(host.textContent).not.toContain('synthetic-one-time-secret')
    app.unmount(); await mount(); expect(host.textContent).not.toContain('synthetic-one-time-secret')
  })
  it('requires cancellation reason and includes expected revision', async () => {
    await mount(); expect(button(en.enrollment.cancel).disabled).toBe(true)
    const reason = host.querySelector('textarea')!; reason.value = 'Operator cancelled'; reason.dispatchEvent(new Event('input')); await flush(); await click(en.enrollment.cancel)
    expect(enrollmentApi.cancel).toHaveBeenCalledWith('request', 3, 'Operator cancelled')
  })
  it('paginates history and displays unconfirmed candidate and revocation recovery without offering activation', async () => {
    const issued: Enrollment = { ...pending, state: 'IssuedUnconfirmed', candidate: { id: 'credential-id', issuanceOperationId: 'stable-operation', state: 'Revoked', bindingVersion: 4, certificateSha256: 'C'.repeat(64), certificatePem: null, rootCertificatePem: '', notBeforeUtc: pending.expiresAtUtc, notAfterUtc: pending.expiresAtUtc, externalRevocationPending: true, lastErrorCode: 'IssuerOutcomeUnknown', canExecuteLineShared: false } }
    vi.mocked(enrollmentApi.list).mockResolvedValueOnce({ items: [issued], total: 2, page: 1, pageSize: 20 }).mockResolvedValueOnce({ items: [{ ...pending, id: 'previous', state: 'Cancelled' }], total: 2, page: 2, pageSize: 20 })
    await mount(); expect(host.textContent).toContain(en.enrollment.state.IssuedUnconfirmed); expect(host.textContent).toContain(en.enrollment.revocationPending)
    expect(host.textContent).toContain('stable-operation'); expect(button(en.enrollment.invite).disabled).toBe(true)
    await click(en.provisioning.more); expect(enrollmentApi.list).toHaveBeenLastCalledWith('device', 2); expect(host.querySelectorAll('article')).toHaveLength(2)
  })
  it('keeps revoked or replaced device history visible while omitting new invitations', async () => {
    await mount('en', { ...device, status: 'Replaced', lineId: null })
    expect(host.querySelectorAll('article')).toHaveLength(1); expect(button(en.enrollment.invite)).toBeUndefined()
  })
})
