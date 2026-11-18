/* eslint-disable vue/one-component-per-file -- Local render stubs isolate Ant Design layout. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, Comment, Text, type App, type PropType } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en'
import { authSession } from '@/services/authSession'
import PicRosterPage from './PicRosterPage.vue'
import type { Pic, PicReceipt } from './model'

const actor = '10000000-0000-0000-0000-000000000001'
const other = '10000000-0000-0000-0000-000000000002'
const picId = '20000000-0000-0000-0000-000000000001'
const first: Pic = { picId, name: 'Current Lee', department: 'Quality', isActive: true, revision: 4 }
const duplicate: Pic = { ...first, picId: '20000000-0000-0000-0000-000000000002' }
const makePage = (items: unknown[], page = 1, hasMore = false) => Response.json({ items, page, pageSize: 20, hasMore })
const api = vi.fn<typeof fetch>()
const endpoint = '/api/admin/pic-roster'
let app: App
let host: HTMLDivElement

function mount(write: boolean) {
  authSession.user.value = {
    id: actor, username: 'test-admin', displayName: 'Admin', role: 'Admin', isActive: true,
    mustChangePassword: false, permissions: write ? ['PicRoster.Read', 'PicRoster.Write'] : ['PicRoster.Read'],
  }
  authSession.accessToken.value = 'test-access'
  authSession.initialized.value = true
  app = createApp(PicRosterPage)
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
  const passthrough = defineComponent({ setup(_, { slots }) { return () => h('div', slots.default?.()) } })
  for (const name of ['a-space', 'a-spin', 'a-tag']) app.component(name, passthrough)
  app.component('AAlert', defineComponent({
    props: { message: { type: String, default: '' } }, setup(props) { return () => h('div', props.message) },
  }))
  app.component('AModal', defineComponent({
    props: { open: Boolean, title: { type: String, default: '' } }, setup(props, { slots }) {
      return () => props.open ? h('div', { role: 'dialog', 'aria-label': props.title }, slots.default?.()) : null
    },
  }))
  app.component('AButton', defineComponent({
    props: { htmlType: { type: String, default: 'button' }, disabled: Boolean }, setup(props, { slots }) {
      return () => h('button', { type: props.htmlType ?? 'button', disabled: props.disabled }, slots.default?.())
    },
  }))
  app.component('AInput', defineComponent({
    props: { value: { type: String, default: '' } }, emits: ['update:value'], setup(props, { emit }) {
      return () => h('input', { value: props.value, onInput: (e: Event) => emit('update:value', (e.target as HTMLInputElement).value) })
    },
  }))
  app.component('ATable', defineComponent({
    props: {
      rowKey: { type: String, required: true }, dataSource: { type: Array as PropType<Record<string, unknown>[]>, required: true },
      columns: { type: Array as PropType<{ key: string }[]>, required: true },
    },
    setup(props, { slots }) {
      return () => h('table', h('tbody', props.dataSource.map(record =>
        h('tr', { 'data-id': record[props.rowKey!] as string }, props.columns.map(column => {
          const children = slots.bodyCell?.({ record, column }).filter(node => node.type !== Comment && (node.type !== Text || String(node.children).trim()))
          return h('td', children?.length ? children : String(record[column.key] ?? ''))
        })),
      )))
    },
  }))
  host = document.createElement('div')
  document.body.append(host)
  app.mount(host)
}

const button = (text: string) => [...host.querySelectorAll('button')].find(node => node.textContent?.trim() === text)
const writeCalls = () => api.mock.calls.filter(([, init]) => init?.method === 'PUT' || init?.method === 'POST')
const expectText = (text: string) => vi.waitFor(() => expect(host.textContent).toContain(text))
function editInput(value: string) {
  const input = host.querySelector<HTMLInputElement>('[role=dialog] input')!
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

beforeEach(() => {
  api.mockReset()
  vi.stubGlobal('fetch', api)
  sessionStorage.clear()
})
afterEach(() => {
  app?.unmount()
  host?.remove()
  authSession.clear()
  vi.unstubAllGlobals()
})

describe('PIC roster page', () => {
  it('uses hasMore, renders same-name records separately and has no mutation actions for read-only Admin', async () => {
    api.mockResolvedValueOnce(makePage([first, duplicate], 1, true)).mockResolvedValueOnce(makePage([], 2))
    mount(false)
    await vi.waitFor(() => expect(host.querySelectorAll('tbody tr')).toHaveLength(2))
    expect(host.querySelector('[data-id="' + duplicate.picId + '"]')).not.toBeNull()
    expect(button('Create PIC')).toBeUndefined()
    expect(button('Edit')).toBeUndefined()
    expect(button('Deactivate')).toBeUndefined()
    expect(button('Previous')?.disabled).toBe(true)
    button('Next')!.click()
    await expectText('Page 2')
    expect(button('Next')?.disabled).toBe(true)
    expect(button('Previous')?.disabled).toBe(false)
    expect(api.mock.calls[1]?.[0]).toBe(endpoint + '?page=2&pageSize=20')
  })

  it('renders immutable history including actor, request identity and New York time', async () => {
    const old: PicReceipt = {
      ...first, revision: 1, name: 'Historical Lee', department: 'Assembly', isActive: false, action: 'Created',
      requestId: '30000000-0000-0000-0000-000000000003', actorUserId: other, actorPersonalSessionId: actor,
      actorUsername: 'historical-admin', environmentId: 'test', occurredAtUtc: '2026-07-01T20:00:00Z', contractVersion: 1,
    }
    api.mockResolvedValueOnce(makePage([first])).mockResolvedValueOnce(makePage([old]))
    mount(false)
    await vi.waitFor(() => expect(button('Revision history')).toBeDefined())
    button('Revision history')!.click()
    await expectText('Historical Lee')
    const row = host.querySelector('[data-id="' + old.requestId + '"]')!
    for (const field of ['Historical Lee', 'Assembly', 'Inactive', 'historical-admin', other, old.requestId, '4:00:00 PM EDT']) {
      expect(row.textContent).toContain(field)
    }
    expect(row.textContent).not.toContain(first.name)
  })

  it('preserves the edit through a 409 and requires reviewing the new revision before another write', async () => {
    const recent = { ...first, revision: 5, name: 'Other admin edit', isActive: false }
    api.mockResolvedValueOnce(makePage([first])).mockResolvedValueOnce(new Response(null, { status: 409 }))
      .mockResolvedValueOnce(Response.json(recent)).mockResolvedValueOnce(new Response(null, { status: 404 }))
      .mockResolvedValueOnce(makePage([recent])).mockResolvedValueOnce(new Response(null, { status: 429 }))
    mount(true)
    await vi.waitFor(() => expect(button('Edit')).toBeDefined())
    button('Edit')!.click()
    await vi.waitFor(() => expect(host.querySelector('[role=dialog]')).not.toBeNull())
    editInput('My proposed name')
    button('Save')!.click()
    await expectText('I reviewed the current revision')
    expect(writeCalls()).toHaveLength(1)
    expect(host.textContent).toContain(recent.name)
    expect(button('Create PIC')?.disabled).toBe(true)
    button('I reviewed the current revision')!.click()
    await vi.waitFor(() => expect(host.querySelector<HTMLInputElement>('[role=dialog] input')?.value).toBe('My proposed name'))
    expect(writeCalls()).toHaveLength(1)
    button('Save')!.click()
    await expectText('Too many requests')
    const [original, second] = writeCalls().map(([, init]) => JSON.parse(String(init?.body)))
    expect(original).toMatchObject({ expectedRevision: 4, name: 'My proposed name' })
    expect(second).toMatchObject({ expectedRevision: 5, name: 'My proposed name' })
    expect(second.requestId).not.toBe(original.requestId)
    expect(second).not.toHaveProperty('isActive')
  })

  it('requires lifecycle confirmation and never shows a failed roster as empty', async () => {
    api.mockResolvedValueOnce(makePage([first])).mockResolvedValueOnce(new Response(null, { status: 503 }))
    mount(true)
    await vi.waitFor(() => expect(button('Deactivate')).toBeDefined())
    button('Deactivate')!.click()
    await expectText('Deactivate Current Lee?')
    expect(writeCalls()).toHaveLength(0)
    button('Cancel')!.click()
    button('Refresh roster')!.click()
    await expectText('temporarily unavailable')
    expect(host.querySelector('table')).toBeNull()
    expect(writeCalls()).toHaveLength(0)
  })
})
