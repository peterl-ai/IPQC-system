/* eslint-disable vue/one-component-per-file -- Test-local component stubs isolate UI behavior. */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import AppShell from './AppShell.vue'
import { authSession } from '@/services/authSession'
import en from '@/i18n/locales/en'
let app: App, host: HTMLDivElement
afterEach(() => { app?.unmount(); host?.remove() })
describe('System Management menu', () => {
  it('shows provisioning only with its server permission and removes it when permissions change', async () => {
    host = document.createElement('div'); document.body.append(host)
    authSession.user.value = { id: 'admin', username: 'admin', displayName: 'Admin', role: 'Admin', isActive: true, mustChangePassword: false, permissions: ['Users.Read', 'DeviceRegistry.Read'] }
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })
    app = createApp(AppShell); app.use(router); app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
    const passthrough = defineComponent({ setup(_, { slots }) { return () => h('div', slots.default?.()) } })
    for (const name of ['a-layout', 'a-layout-sider', 'a-layout-header', 'a-layout-content', 'a-select', 'a-select-option', 'a-divider', 'a-avatar', 'a-button']) app.component(name, passthrough)
    app.component('AMenu', defineComponent({ props: { items: { type: Array, default: () => [] } }, setup(p) { return () => h('nav', JSON.stringify(p.items)) } }))
    await router.isReady(); app.mount(host); await nextTick()
    expect(host.querySelector('nav')?.textContent).toContain('/provisioning'); expect(host.querySelector('nav')?.textContent).toContain('/users')
    authSession.user.value = { ...authSession.user.value, role: 'PQE', permissions: ['PatrolStandards.Read'] }; await nextTick()
    expect(host.querySelector('nav')?.textContent).not.toContain('/provisioning'); expect(host.querySelector('nav')?.textContent).not.toContain('/users')
  })
})
