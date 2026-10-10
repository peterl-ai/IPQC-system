<template>
  <a-layout class="app-shell">
    <a-layout-sider v-model:collapsed="collapsed" collapsible :width="252" :collapsed-width="72" class="app-sider">
      <div class="brand" :class="{ compact: collapsed }">
        <div class="brand-mark">JX</div>
        <div v-if="!collapsed" class="brand-copy">
          <strong>{{ t('product.name') }}</strong>
          <span>{{ t('product.environment') }}</span>
        </div>
      </div>
      <a-menu v-model:selectedKeys="selectedKeys" mode="inline" theme="dark" :items="menuItems" @click="onMenuClick" />
    </a-layout-sider>

    <a-layout>
      <a-layout-header class="app-header">
        <div class="header-context">
          <span class="header-kicker">{{ t('nav.linePatrol') }}</span>
          <strong>{{ currentTitle }}</strong>
        </div>
        <div class="header-actions">
          <div class="control-cluster">
            <GlobalOutlined />
            <a-select :value="locale" size="small" style="width: 106px" @change="changeLanguage">
              <a-select-option value="en">English</a-select-option>
              <a-select-option value="zh-CN">中文</a-select-option>
            </a-select>
          </div>
          <a-divider type="vertical" />
          <div class="user-chip">
            <a-avatar>{{ initials }}</a-avatar>
            <div><span>{{ t('header.user') }}</span><strong>{{ authSession.user.value?.displayName }}</strong></div>
          </div>
          <a-button type="text" size="small" @click="logout"><LogoutOutlined />{{ t('header.logout') }}</a-button>
        </div>
      </a-layout-header>
      <a-layout-content class="app-content">
        <router-view />
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>

<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import {
  ApartmentOutlined, AuditOutlined, BookOutlined, CheckSquareOutlined, FileDoneOutlined,
  GlobalOutlined, HomeOutlined, LogoutOutlined, SafetyCertificateOutlined, SettingOutlined, TeamOutlined,
} from '@ant-design/icons-vue'
import type { ItemType, MenuProps } from 'ant-design-vue'
import { saveLocale, type SupportedLocale } from '@/i18n'
import { hasPermission } from '@/services/permissions'
import { authSession } from '@/services/authSession'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const collapsed = ref(false)
const selectedKeys = ref<string[]>([route.path])

const icon = (component: object) => () => h(component)
const menuItems = computed<ItemType[]>(() => {
  const permissions = authSession.user.value?.permissions ?? []
  const items: ItemType[] = []
  if (hasPermission(permissions, 'home:view')) items.push({ key: '/home', icon: icon(HomeOutlined), label: t('nav.home') })
  const patrolChildren: ItemType[] = [
    hasPermission(permissions, 'standards:view') ? { key: '/standards', icon: icon(BookOutlined), label: t('nav.standards') } : null,
    hasPermission(permissions, 'plans:view') ? { key: '/plans', icon: icon(ApartmentOutlined), label: t('nav.plans') } : null,
    hasPermission(permissions, 'tasks:view') ? { key: '/tasks', icon: icon(CheckSquareOutlined), label: t('nav.tasks') } : null,
    hasPermission(permissions, 'records:view') ? { key: '/records', icon: icon(FileDoneOutlined), label: t('nav.records') } : null,
  ].filter(Boolean) as ItemType[]
  if (patrolChildren.length) items.push({ key: 'ipqc', icon: icon(SafetyCertificateOutlined), label: t('nav.ipqc'), children: [{ key: 'line-patrol', icon: icon(AuditOutlined), label: t('nav.linePatrol'), children: patrolChildren }] })
  if (hasPermission(permissions, 'users:view')) items.push({ key: 'system', icon: icon(SettingOutlined), label: t('nav.system'), children: [{ key: '/users', icon: icon(TeamOutlined), label: t('nav.users') }] })
  return items
})

const titleKeys: Record<string, string> = { '/home': 'nav.home', '/standards': 'nav.standards', '/plans': 'nav.plans', '/tasks': 'nav.tasks', '/records': 'nav.records', '/users': 'nav.users' }
const currentTitle = computed(() => t(titleKeys[route.path] ?? 'product.name'))
const initials = computed(() => (authSession.user.value?.displayName ?? '').split(' ').map((part) => part[0]).join(''))

watch(() => route.path, (path) => { selectedKeys.value = [path] })

function onMenuClick(info: Parameters<NonNullable<MenuProps['onClick']>>[0]) {
  if (typeof info.key === 'string' && info.key.startsWith('/')) router.push(info.key)
}

function changeLanguage(next: SupportedLocale) {
  locale.value = next
  saveLocale(next)
}

async function logout() { await authSession.logout(); await router.replace('/login') }
</script>
