<template>
  <div class="data-panel">
    <a-alert type="info" show-icon :message="t('sharedPlan.notice')" />
    <a-alert v-if="error" role="alert" type="error" show-icon :message="error" />
    <a-space class="table-toolbar"><a-button :disabled="busy" @click="reload">{{ t('common.reset') }}</a-button><a-button type="primary" :disabled="busy || lines.length !== 2" @click="openNew">{{ t('action.new') }}</a-button></a-space>
    <SharedPlanConflictPanel v-if="conflict?.mode === 'toggle'" :latest="conflict.latest" :draft="null" :refresh-error="conflict.refreshError" :refreshing="conflict.refreshing" :acknowledged="false" :busy="busy" @refresh="refreshConflict" />
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
    <a-drawer v-model:open="editor" :title="t('sharedPlan.title')" width="min(650px, 94vw)" :mask-closable="false" :closable="!busy" :keyboard="!busy" @close="closeEditor">
      <a-alert v-if="error" role="alert" type="error" :message="error" />
      <SharedPlanConflictPanel v-if="conflict?.mode === 'edit'" :latest="conflict.latest" :draft="draft" :refresh-error="conflict.refreshError" :refreshing="conflict.refreshing" :acknowledged="conflict.acknowledged" :busy="busy" @refresh="refreshConflict" @acknowledge="acknowledgeLatest" @load-server="loadServerVersion" />
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
      <template #footer><a-space><a-button type="primary" :loading="busy" :disabled="busy || saveBlocked" @click="save">{{ t('action.save') }}</a-button><a-button :disabled="busy" @click="closeEditor">{{ t('common.cancel') }}</a-button></a-space></template>
    </a-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { lineSharedApi, SharedPlanError, type SharedLine, type SharedPlan, type SharedPlanDraft } from './lineSharedApi'
import { patrolPlansApi, type StandardOption } from './api'
import { validateSharedPlan } from './lineSharedValidation'
import SharedPlanConflictPanel from './SharedPlanConflictPanel.vue'
const { t } = useI18n()
const rows = ref<SharedPlan[]>([]), lines = ref<SharedLine[]>([]), standards = ref<StandardOption[]>([])
const page = ref(1), total = ref(0), busy = ref(false), error = ref(''), editor = ref(false), editingId = ref<string | null>(null)
interface RevisionConflict { id: string; mode: 'edit' | 'toggle'; latest: SharedPlan | null; refreshError: string; refreshing: boolean; acknowledged: boolean }
const conflict = ref<RevisionConflict | null>(null)
let refreshGeneration = 0
const saveBlocked = computed(() => conflict.value?.mode === 'edit' && !conflict.value.acknowledged)
const blank = (): SharedPlanDraft => ({ planNo: '', planName: '', patrolStandardId: '', lineId: '', rounds: ['Day', 'Night'], effectiveStartLocal: '', effectiveEndLocal: null, isEnabled: false })
const draft = reactive<SharedPlanDraft>(blank())
const columns = computed(() => [
  { title: t('field.planNo'), dataIndex: 'planNo' }, { title: t('field.planName'), dataIndex: 'planName' },
  { title: t('field.line'), key: 'line' }, { title: t('sharedPlan.rounds'), key: 'rounds' },
  { title: t('sharedPlan.revision'), dataIndex: 'sharedPlanRevision' }, { title: t('field.status'), key: 'status' }, { title: t('common.actions'), key: 'actions' },
])
function errorText(e: unknown): string {
  if (e instanceof SharedPlanError) {
    if (e.status === 401) return t('sharedPlan.loginRequired')
    if (e.status === 403) return t('sharedPlan.Forbidden')
    if (e.status === 404) return t('sharedPlan.planMissing')
    const codes = ['InvalidSharedPlanInput', 'SharedStandardNotReady', 'LineConfigurationUnavailable', 'PersonalAccessConfigurationUnavailable', 'SharedPlanOverlap', 'DuplicateSharedPlanNo', 'SharedPlanUnadmitted', 'Forbidden']
    if (codes.includes(e.code)) return t(`sharedPlan.${e.code}`)
    if (e.status === 409) return t('sharedPlan.conflict')
  }
  return t('sharedPlan.Unavailable')
}
function clearConflict() { refreshGeneration++; conflict.value = null }
function closeEditor() { editor.value = false; clearConflict() }
function applyServer(row: SharedPlan) {
  Object.assign(draft, { planNo: row.planNo, planName: row.planName, patrolStandardId: row.patrolStandardId, lineId: row.lineId ?? '', rounds: [...row.rounds], effectiveStartLocal: row.effectiveStartLocal, effectiveEndLocal: row.effectiveEndLocal, isEnabled: row.isEnabled, expectedRevision: row.sharedPlanRevision ?? undefined })
}
function openNew() { clearConflict(); editingId.value = null; Object.assign(draft, blank()); delete draft.expectedRevision; editor.value = true; error.value = '' }
function edit(row: SharedPlan) { clearConflict(); editingId.value = row.id; applyServer(row); editor.value = true; error.value = '' }
async function refreshConflict() {
  const current = conflict.value
  if (!current) return
  const generation = ++refreshGeneration
  current.refreshing = true; current.acknowledged = false; current.latest = null; current.refreshError = ''; busy.value = true
  try {
    const latest = await lineSharedApi.get(current.id)
    if (generation !== refreshGeneration) return
    current.latest = latest
    // Refresh only the affected authoritative row; never copy this response into the draft.
    rows.value = rows.value.map(row => row.id === latest.id ? latest : row)
  } catch (e) {
    if (generation !== refreshGeneration) return
    current.refreshError = errorText(e)
    if (e instanceof SharedPlanError && e.status === 404) {
      const existed = rows.value.some(row => row.id === current.id)
      rows.value = rows.value.filter(row => row.id !== current.id)
      if (existed) total.value = Math.max(0, total.value - 1)
    }
  } finally {
    if (generation === refreshGeneration) { current.refreshing = false; busy.value = false }
  }
}
async function writeError(e: unknown, id: string | null, mode: 'edit' | 'toggle') {
  error.value = errorText(e)
  if (e instanceof SharedPlanError && e.status === 409 && e.code === 'SharedPlanConflict' && id) {
    conflict.value = { id, mode, latest: null, refreshError: '', refreshing: false, acknowledged: false }
    await refreshConflict()
  } else if (conflict.value?.mode === 'edit') {
    conflict.value.acknowledged = false
  }
}
function acknowledgeLatest() {
  const latest = conflict.value?.latest
  if (conflict.value?.mode !== 'edit' || !latest || latest.configurationStatus !== 'Admitted' || !Number.isSafeInteger(latest.sharedPlanRevision) || (latest.sharedPlanRevision ?? 0) <= 0) return
  // Explicit acknowledgement changes only the revision. No write is retried here.
  draft.expectedRevision = latest.sharedPlanRevision!
  conflict.value.acknowledged = true; error.value = ''
}
function loadServerVersion() {
  const latest = conflict.value?.latest
  if (!latest) return
  acknowledgeLatest()
  if (conflict.value?.acknowledged) applyServer(latest)
}
async function reload() {
  busy.value = true; error.value = ''
  try { const result = await lineSharedApi.list(page.value); rows.value = result.items; total.value = result.total }
  catch (e) { error.value = errorText(e) }
  try { lines.value = await lineSharedApi.lines() } catch (e) { lines.value = []; error.value = errorText(e) }
  busy.value = false
}
async function changePage(next: number) { page.value = next; await reload() }
async function save() {
  if (busy.value || saveBlocked.value) return
  if (!validateSharedPlan(draft, lines.value.map(x => x.id))) { error.value = t('sharedPlan.InvalidSharedPlanInput'); return }
  busy.value = true
  try { await lineSharedApi.save(editingId.value, { ...draft, effectiveEndLocal: draft.effectiveEndLocal || null }); closeEditor(); await reload() }
  catch (e) { await writeError(e, editingId.value, 'edit') } finally { busy.value = false }
}
async function toggle(row: SharedPlan) {
  clearConflict(); busy.value = true
  try { await lineSharedApi.toggle(row); await reload() } catch (e) { await writeError(e, row.id, 'toggle') } finally { busy.value = false }
}
onMounted(async () => { await reload(); try { standards.value = await patrolPlansApi.standards() } catch (e) { error.value = errorText(e) } })
</script>
