<template>
  <div class="home-page">
    <section class="home-hero">
      <div class="hero-copy">
        <p class="page-eyebrow">{{ t('home.eyebrow') }}</p>
        <h1>{{ t('home.title') }}</h1>
        <p>{{ t('home.description') }}</p>
        <a-button v-if="canOpenStandards" type="primary" size="large" @click="router.push('/standards')">
          {{ t('home.enter') }} <ArrowRightOutlined />
        </a-button>
        <a-button v-else type="primary" size="large" @click="router.push('/records')">
          {{ t('nav.records') }} <ArrowRightOutlined />
        </a-button>
      </div>
      <div class="hero-grid" aria-hidden="true">
        <div v-for="index in 24" :key="index" :class="{ active: [3, 8, 9, 15, 20].includes(index) }" />
      </div>
    </section>
    <section class="home-panels">
      <article>
        <span>01</span>
        <h2>{{ t('home.scope') }}</h2>
        <p>{{ t('home.scopeText') }}</p>
      </article>
      <article class="workflow-panel">
        <span>02</span>
        <h2>{{ t('home.workflow') }}</h2>
        <p>{{ t('home.workflowText') }}</p>
        <div class="workflow-steps">
          <div v-for="step in steps" :key="step"><i />{{ step }}</div>
        </div>
      </article>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ArrowRightOutlined } from '@ant-design/icons-vue'
import { hasPermission } from '@/services/permissions'
import { mockSession } from '@/services/mockSession'

const { t } = useI18n()
const router = useRouter()
const canOpenStandards = computed(() => hasPermission(mockSession.role.value, 'standards:view'))
const steps = computed(() => [t('home.step1'), t('home.step2'), t('home.step3'), t('home.step4')])
</script>
