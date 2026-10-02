<template>
  <div class="status-page">
    <div class="status-code">{{ t(`${kind}.code`) }}</div>
    <h1>{{ t(`${kind}.title`) }}</h1>
    <p>{{ t(`${kind}.description`) }}</p>
    <a-button type="primary" @click="goBack">{{ t(`${kind}.back`) }}</a-button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { defaultRouteForRole } from '@/services/permissions'
import { mockSession } from '@/services/mockSession'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const kind = computed(() => route.meta.status === 'notFound' ? 'notFound' : 'permission')
function goBack() { router.push(defaultRouteForRole(mockSession.role.value)) }
</script>
