<template>
  <a-config-provider :locale="antLocale" :theme="theme">
    <router-view />
  </a-config-provider>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import enUS from 'ant-design-vue/es/locale/en_US'
import zhCN from 'ant-design-vue/es/locale/zh_CN'
import { useI18n } from 'vue-i18n'

const { locale } = useI18n()
const antLocale = computed(() => (locale.value === 'zh-CN' ? zhCN : enUS))
const theme = {
  token: {
    colorPrimary: '#087ea4',
    colorInfo: '#087ea4',
    colorSuccess: '#16845b',
    colorWarning: '#c97913',
    colorError: '#cf3f4f',
    borderRadius: 5,
    fontFamily: "'IBM Plex Sans', 'Noto Sans SC', 'Segoe UI', sans-serif",
  },
}
</script>
