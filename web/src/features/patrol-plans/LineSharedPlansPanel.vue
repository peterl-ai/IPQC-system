<template>
  <div class="data-panel">
    <a-alert type="info" show-icon :message="t('sharedPlan.notice')" />
    <a-alert v-if="error" type="error" show-icon :message="error" />
    <a-space class="table-toolbar"><a-button @click="reload">{{ t('common.reset') }}</a-button><a-button type="primary" :disabled="busy || lines.length !== 2" @click="openNew">{{ t('action.new') }}</a-button></a-space>
    <a-table row-key="id" :columns="columns" :data-source="rows" :loading="busy" :pagination="{ current: page, pageSize: 10, total, onChange: changePage }" :scroll="{ x: 950 }">
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'line'">{{ lines.find(x => x.id === record.lineId)?.name ?? record.lineId }}</template>
        <template v-else-if="column.key === 'rounds'">{{ record.rounds.map((x: string) => t(`sharedPlan.${x}`)).join(', ') }}</template>
        <template v-else-if="column.key === 'status'">{{ t(record.configurationStatus === 'HistoricalUnadmitted' ? 'sharedPlan.historical' : record.isEnabled ? 'common.enabled' : 'common.disabled') }}</template>
        <template v-else-if="column.key === 'actions'">
          <a-space>
            <a-button :disabled="!record.sharedPlanRevision || busy" @click="edit(record)">{{ t('common.edit') }}</a-button>
            <a-button :disabled="!record.sharedPlanRevision || busy" @click="toggle(record)">{{ t(record.isEnabled ? 'plan.disable' : 'plan.enable') }}</a-button>
          </a-space>
        </template>
      </template>
    </a-table>
    <a-drawer v-model:open="editor" :title="t('sharedPlan.title')" width="min(650px, 94vw)" :mask-closable="false">
      <a-alert v-if="error" type="error" :message="error" />
      <a-form layout="vertical">
        <a-form-item :label="t('field.planNo')"><a-input v-model:value="draft.planNo" :maxlength="50" /></a-form-item>
        <a-form-item :label="t('field.planName')" required><a-input v-model:value="draft.planName" :maxlength="200" /></a-form-item>
        <a-form-item :label="t('field.standard')" required><a-select v-model:value="draft.patrolStandardId" :options="standards.map(x => ({ value: x.id, label: x.patrolStandardName }))" /></a-form-item>
        <a-form-item :label="t('field.line')" required><a-select v-model:value="draft.lineId" :options="lines.map(x => ({ value: x.id, label: `${x.code} / ${x.name}` }))" /></a-form-item>
        <a-form-item :label="t('sharedPlan.rounds')" required><a-checkbox-group v-model:value="draft.rounds" :options="[{ value: 'Day', label: t('sharedPlan.Day') }, { value: 'Night', label: t('sharedPlan.Night') }]" /></a-form-item>
        <a-form-item :label="t('field.effectiveStart')" required><a-input v-model:value="draft.effectiveStartLocal" type="datetime-local" /></a-form-item>
        <a-form-item :label="t('field.effectiveEnd')"><a-input v-model:value="draft.effectiveEndLocal" type="datetime-local" /></a-form-item>
        <a-form-item :label="t('sharedPlan.scheduling')"><a-switch v-model:checked="draft.isEnabled" /></a-form-item>
      </a-form>
      <template #footer><a-space><a-button type="primary" :loading="busy" @click="save">{{ t('action.save') }}</a-button><a-button @click="editor = false">{{ t('common.cancel') }}</a-button></a-space></template>
    </a-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { lineSharedApi, SharedPlanError, type SharedLine, type SharedPlan, type SharedPlanDraft } from './lineSharedApi'
import { patrolPlansApi, type StandardOption } from './api'
import { validateSharedPlan } from './lineSharedValidation'
const { t } = useI18n()
const rows = ref<SharedPlan[]>([]), lines = ref<SharedLine[]>([]), standards = ref<StandardOption[]>([])
const page = ref(1), total = ref(0), busy = ref(false), error = ref(''), editor = ref(false), editingId = ref<string | null>(null)
const blank = (): SharedPlanDraft => ({ planNo: '', planName: '', patrolStandardId: '', lineId: '', rounds: ['Day', 'Night'], effectiveStartLocal: '', effectiveEndLocal: null, isEnabled: false })
const draft = reactive<SharedPlanDraft>(blank())
const columns = computed(() => [
  { title: t('field.planNo'), dataIndex: 'planNo' }, { title: t('field.planName'), dataIndex: 'planName' },
  { title: t('field.line'), key: 'line' }, { title: t('sharedPlan.rounds'), key: 'rounds' },
  { title: t('sharedPlan.revision'), dataIndex: 'sharedPlanRevision' }, { title: t('field.status'), key: 'status' }, { title: t('common.actions'), key: 'actions' },
])
function showError(e: unknown) {
  if (e instanceof SharedPlanError) {
    if (e.status === 409) { error.value = t('sharedPlan.conflict'); return }
    if (e.status === 401) { error.value = t('sharedPlan.loginRequired'); return }
    const codes = ['InvalidSharedPlanInput', 'SharedStandardNotReady', 'LineConfigurationUnavailable', 'PersonalAccessConfigurationUnavailable', 'Forbidden']
    error.value = codes.includes(e.code) ? t(`sharedPlan.${e.code}`) : t('sharedPlan.Unavailable')
  } else error.value = t('sharedPlan.Unavailable')
}
async function reload() {
  busy.value = true; error.value = ''
  // Inventory failure must not hide diagnosable plans or prevent disabling a stale configuration.
  try { const result = await lineSharedApi.list(page.value); rows.value = result.items; total.value = result.total }
  catch (e) { showError(e) }
  try { lines.value = await lineSharedApi.lines() } catch (e) { lines.value = []; showError(e) }
  busy.value = false
}
async function changePage(next: number) { page.value = next; await reload() }
function openNew() { editingId.value = null; Object.assign(draft, blank()); delete draft.expectedRevision; editor.value = true; error.value = '' }
function edit(row: SharedPlan) {
  editingId.value = row.id
  Object.assign(draft, { planNo: row.planNo, planName: row.planName, patrolStandardId: row.patrolStandardId, lineId: row.lineId ?? '', rounds: [...row.rounds], effectiveStartLocal: row.effectiveStartLocal, effectiveEndLocal: row.effectiveEndLocal, isEnabled: row.isEnabled, expectedRevision: row.sharedPlanRevision ?? undefined })
  editor.value = true; error.value = ''
}
async function save() {
  if (!validateSharedPlan(draft, lines.value.map(x => x.id))) { error.value = t('sharedPlan.InvalidSharedPlanInput'); return }
  busy.value = true
  try { await lineSharedApi.save(editingId.value, { ...draft, effectiveEndLocal: draft.effectiveEndLocal || null }); editor.value = false; await reload() }
  catch (e) { showError(e) } finally { busy.value = false }
}
async function toggle(row: SharedPlan) {
  busy.value = true
  try { await lineSharedApi.toggle(row); await reload() } catch (e) { showError(e) } finally { busy.value = false }
}
onMounted(async () => { await reload(); try { standards.value = await patrolPlansApi.standards() } catch (e) { showError(e) } })
</script>
