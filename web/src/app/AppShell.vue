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
      <div v-if="!collapsed" class="sider-foot">{{ t('common.mockNotice') }}</div>
    </a-layout-sider>

    <a-layout>
      <a-layout-header class="app-header">
        <div class="header-context">
          <span class="header-kicker">{{ t('nav.linePatrol') }}</span>
          <strong>{{ currentTitle }}</strong>
        </div>
        <div class="header-actions">
          <div v-if="isDevelopment" class="control-cluster">
            <span>{{ t('header.role') }}</span>
            <a-select :value="mockSession.role.value" size="small" style="width: 112px" @change="changeRole">
              <a-select-option value="admin">{{ t('role.admin') }}</a-select-option>
              <a-select-option value="pqe">{{ t('role.pqe') }}</a-select-option>
              <a-select-option value="ipqa">{{ t('role.ipqa') }}</a-select-option>
            </a-select>
          </div>
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
            <div><span>{{ t('header.user') }}</span><strong>{{ mockSession.userName.value }}</strong></div>
          </div>
          <a-button type="text" size="small"><LogoutOutlined />{{ t('header.logout') }}</a-button>
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
import { mockSession } from '@/services/mockSession'
import type { Role } from '@/types/auth'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const collapsed = ref(false)
const selectedKeys = ref<string[]>([route.path])
const isDevelopment = import.meta.env.DEV

const icon = (component: object) => () => h(component)
const menuItems = computed<ItemType[]>(() => {
  const role = mockSession.role.value
  const items: ItemType[] = []
  if (hasPermission(role, 'home:view')) items.push({ key: '/home', icon: icon(HomeOutlined), label: t('nav.home') })
  const patrolChildren: ItemType[] = [
    hasPermission(role, 'standards:view') ? { key: '/standards', icon: icon(BookOutlined), label: t('nav.standards') } : null,
    hasPermission(role, 'plans:view') ? { key: '/plans', icon: icon(ApartmentOutlined), label: t('nav.plans') } : null,
    hasPermission(role, 'tasks:view') ? { key: '/tasks', icon: icon(CheckSquareOutlined), label: t('nav.tasks') } : null,
    hasPermission(role, 'records:view') ? { key: '/records', icon: icon(FileDoneOutlined), label: t('nav.records') } : null,
  ].filter(Boolean) as ItemType[]
  if (patrolChildren.length) items.push({ key: 'ipqc', icon: icon(SafetyCertificateOutlined), label: t('nav.ipqc'), children: [{ key: 'line-patrol', icon: icon(AuditOutlined), label: t('nav.linePatrol'), children: patrolChildren }] })
  if (hasPermission(role, 'users:view')) items.push({ key: 'system', icon: icon(SettingOutlined), label: t('nav.system'), children: [{ key: '/users', icon: icon(TeamOutlined), label: t('nav.users') }] })
  return items
})

const titleKeys: Record<string, string> = { '/home': 'nav.home', '/standards': 'nav.standards', '/plans': 'nav.plans', '/tasks': 'nav.tasks', '/records': 'nav.records', '/users': 'nav.users' }
const currentTitle = computed(() => t(titleKeys[route.path] ?? 'product.name'))
const initials = computed(() => mockSession.userName.value.split(' ').map((part) => part[0]).join(''))

watch(() => route.path, (path) => { selectedKeys.value = [path] })

function onMenuClick(info: Parameters<NonNullable<MenuProps['onClick']>>[0]) {
  if (typeof info.key === 'string' && info.key.startsWith('/')) router.push(info.key)
}

function changeLanguage(next: SupportedLocale) {
  locale.value = next
  saveLocale(next)
}

async function changeRole(next: Role) {
  mockSession.setRole(next)
  const permission = route.meta.permission
  if (!hasPermission(next, permission)) await router.push(next === 'ipqa' ? '/records' : '/home')
}
</script>
