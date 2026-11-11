<template>
  <section class="shared-plan-reconciliation" role="region" :aria-label="t('sharedPlan.reconcileTitle')">
    <h3>{{ t('sharedPlan.reconcileTitle') }}</h3>
    <p role="status">{{ t(draft ? 'sharedPlan.reconcileDraft' : 'sharedPlan.reconcileToggle') }}</p>
    <a-alert v-if="refreshError" role="alert" type="error" :message="refreshError" />
    <p v-if="refreshing" role="status">{{ t('sharedPlan.refreshing') }}</p>
    <template v-if="latest">
      <p role="status">{{ t('sharedPlan.serverRevision', { revision: latest.sharedPlanRevision ?? t('sharedPlan.historical') }) }}</p>
      <div class="comparison-scroll">
        <table>
          <caption>{{ t('sharedPlan.compareValues') }}</caption>
          <thead><tr><th scope="col">{{ t('sharedPlan.valueField') }}</th><th v-if="draft" scope="col">{{ t('sharedPlan.yourDraft') }}</th><th scope="col">{{ t('sharedPlan.serverValues') }}</th></tr></thead>
          <tbody>
            <tr v-for="value in values" :key="value.label" :data-different="draft && value.draft !== value.server">
              <th scope="row">{{ value.label }}</th><td v-if="draft">{{ value.draft }}</td><td>{{ value.server }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="acknowledged" role="status">{{ t('sharedPlan.revisionAcknowledged') }}</p>
      <a-space v-if="draft">
        <a-button :disabled="busy || !canAcknowledge || acknowledged" @click="$emit('acknowledge')">{{ t('sharedPlan.keepDraftAcknowledge', { revision: latest.sharedPlanRevision }) }}</a-button>
        <a-button :disabled="busy || !canAcknowledge" @click="$emit('loadServer')">{{ t('sharedPlan.loadServer') }}</a-button>
      </a-space>
    </template>
    <a-button :disabled="busy" @click="$emit('refresh')">{{ t('sharedPlan.refreshCurrent') }}</a-button>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SharedPlan, SharedPlanDraft } from './lineSharedApi'
const props = defineProps<{ latest: SharedPlan | null; draft: SharedPlanDraft | null; refreshError: string; refreshing: boolean; acknowledged: boolean; busy: boolean }>()
defineEmits<{ acknowledge: []; loadServer: []; refresh: [] }>()
const { t } = useI18n()
const canAcknowledge = computed(() => props.latest?.configurationStatus === 'Admitted' && Number.isSafeInteger(props.latest.sharedPlanRevision) && (props.latest.sharedPlanRevision ?? 0) > 0)
const rounds = (value?: string[]) => value?.map(x => x === 'Day' || x === 'Night' ? t(`sharedPlan.${x}`) : x).join(', ') ?? ''
const values = computed(() => [
  { label: t('field.planNo'), draft: props.draft?.planNo, server: props.latest?.planNo },
  { label: t('field.planName'), draft: props.draft?.planName, server: props.latest?.planName },
  { label: t('field.standard'), draft: props.draft?.patrolStandardId, server: props.latest?.patrolStandardId },
  { label: t('field.line'), draft: props.draft?.lineId, server: props.latest?.lineId },
  { label: t('sharedPlan.rounds'), draft: rounds(props.draft?.rounds), server: rounds(props.latest?.rounds) },
  { label: t('field.effectiveStart'), draft: props.draft?.effectiveStartLocal, server: props.latest?.effectiveStartLocal },
  { label: t('field.effectiveEnd'), draft: props.draft?.effectiveEndLocal, server: props.latest?.effectiveEndLocal },
  { label: t('sharedPlan.scheduling'), draft: t(props.draft?.isEnabled ? 'common.enabled' : 'common.disabled'), server: t(props.latest?.isEnabled ? 'common.enabled' : 'common.disabled') },
  { label: t('sharedPlan.revision'), draft: props.draft?.expectedRevision, server: props.latest?.sharedPlanRevision },
])
</script>

<style scoped>
.shared-plan-reconciliation { margin-block: 16px; padding: 12px; border: 1px solid var(--color-border, #b6bcc6); }
.comparison-scroll { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; margin-block: 12px; }
th, td { text-align: start; padding: 6px; border-bottom: 1px solid #d9d9d9; overflow-wrap: anywhere; }
tr[data-different='true'] td { font-weight: 600; }
</style>
