<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.plans.title')" :subtitle="t('page.plans.subtitle')" />
    <div class="data-panel">
      <a-alert v-if="error" type="error" show-icon :message="error" />
      <div class="filter-bar">
        <a-form layout="inline" @finish="search">
          <a-form-item :label="t('field.planNo')"><a-input v-model:value="filters.planNo" allow-clear /></a-form-item>
          <a-form-item :label="t('field.planName')"><a-input v-model:value="filters.planName" allow-clear /></a-form-item>
          <a-form-item :label="t('field.standard')"><a-select v-model:value="filters.patrolStandardId" allow-clear show-search option-filter-prop="label" :options="standardOptions" style="width: 210px" /></a-form-item>
          <a-form-item :label="t('field.factory')"><a-input v-model:value="filters.factoryCode" allow-clear /></a-form-item>
          <a-form-item :label="t('field.line')"><a-input v-model:value="filters.lineCode" allow-clear /></a-form-item>
          <a-form-item :label="t('field.status')"><a-select v-model:value="filters.enabled" allow-clear :options="statusOptions" style="width: 125px" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary" @click="search"><SearchOutlined />{{ t('common.search') }}</a-button><a-button @click="reset"><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar"><a-button type="primary" :disabled="busy" @click="openNew"><PlusOutlined />{{ t('action.new') }}</a-button></div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="plans" :loading="loading" :scroll="{ x: 1750 }" :pagination="pagination">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'isEnabled'"><StatusTag :tone="record.isEnabled ? 'success' : 'neutral'" :label="record.isEnabled ? t('common.enabled') : t('common.disabled')" /></template>
          <template v-else-if="column.key === 'schedule'">{{ scheduleText(record.schedule) }}</template>
          <template v-else-if="column.key === 'assigneeKey'">{{ assigneeName(record.assigneeKey) }}</template>
          <template v-else-if="column.key === 'updatedAtUtc'">{{ plantDate(record.updatedAtUtc) }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openEdit(record.id)">{{ t('common.edit') }}</a-button>
              <a-popconfirm :title="record.isEnabled ? t('plan.disableConfirm') : t('plan.enableConfirm')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @confirm="toggle(record)"><a-button type="link" size="small">{{ record.isEnabled ? t('plan.disable') : t('plan.enable') }}</a-button></a-popconfirm>
              <a-popconfirm :title="t('plan.deleteConfirm')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @confirm="remove(record.id)"><a-button type="link" size="small" danger>{{ t('common.delete') }}</a-button></a-popconfirm>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="editorOpen" :title="editingId ? t('plan.editTitle') : t('plan.createTitle')" width="min(760px, 94vw)" :mask-closable="false">
      <h3 class="section-title">{{ t('plan.information') }}</h3>
      <a-form layout="vertical" class="plan-form">
        <a-form-item :label="t('field.planNo')"><a-input v-model:value="draft.planNo" :placeholder="t('plan.autoNumber')" /></a-form-item>
        <a-form-item :label="t('field.planName')" required><a-input v-model:value="draft.planName" /></a-form-item>
        <a-form-item :label="t('field.standard')" required><a-select v-model:value="draft.patrolStandardId" show-search option-filter-prop="label" :options="standardOptions" @change="standardChanged" /></a-form-item>
        <a-form-item :label="t('field.factoryCode')"><a-input v-model:value="draft.factoryCode" /></a-form-item>
        <a-form-item :label="t('field.factoryName')"><a-input v-model:value="draft.factoryName" /></a-form-item>
        <a-form-item :label="t('field.lineCode')"><a-input v-model:value="draft.lineCode" /></a-form-item>
        <a-form-item :label="t('field.lineName')"><a-input v-model:value="draft.lineName" /></a-form-item>
        <a-form-item :label="t('field.assignedIpqa')" :required="draft.isEnabled"><a-select v-model:value="draft.assigneeKey" allow-clear :options="assigneeOptions" /></a-form-item>
        <a-form-item :label="t('common.enabled')"><a-switch v-model:checked="draft.isEnabled" /></a-form-item>
      </a-form>
      <h3 class="section-title">{{ t('plan.scheduleSection') }}</h3>
      <a-form layout="vertical" class="plan-form">
        <a-form-item :label="t('field.effectiveStart')" required><a-input v-model:value="draft.effectiveStartLocal" type="datetime-local" /></a-form-item>
        <a-form-item :label="t('field.effectiveEnd')"><a-input v-model:value="draft.effectiveEndLocal" type="datetime-local" /></a-form-item>
        <a-form-item :label="t('plan.timezone')"><a-input :value="draft.timeZoneId" disabled /></a-form-item>
        <a-form-item :label="t('plan.frequency')"><a-select v-model:value="draft.schedule.type" :options="frequencyOptions" @change="frequencyChanged" /></a-form-item>
        <a-form-item v-if="draft.schedule.type === 'everyNHours'" :label="t('plan.intervalHours')"><a-input-number v-model:value="draft.schedule.intervalHours" :min="1" :max="168" /></a-form-item>
        <template v-else>
          <a-form-item v-if="draft.schedule.type === 'weekly'" :label="t('plan.weekdays')"><a-checkbox-group v-model:value="weekdays" :options="weekdayOptions" /></a-form-item>
          <a-form-item :label="t('plan.times')"><div v-for="(_, index) in times" :key="index" class="plan-time-row"><a-input v-model:value="times[index]" type="time" /><a-button :disabled="times.length === 1" @click="times.splice(index, 1)">{{ t('plan.removeTime') }}</a-button></div><a-button :disabled="times.length >= 24" @click="times.push('08:00')">{{ t('plan.addTime') }}</a-button></a-form-item>
        </template>
      </a-form>
      <template #footer><a-space><a-button type="primary" :loading="busy" @click="save">{{ t('action.save') }}</a-button><a-button @click="editorOpen = false">{{ t('common.cancel') }}</a-button></a-space></template>
    </a-drawer>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { message, type TableColumnsType } from 'ant-design-vue'
import { PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { patrolPlansApi, type Assignee, type Plan, type PlanDraft, type ScheduleDefinition, type StandardOption } from './api'
import { validatePlanDraft } from './validation'

const { t } = useI18n()
const plans = ref<Plan[]>([])
const standards = ref<StandardOption[]>([])
const assignees = ref<Assignee[]>([])
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const filters = reactive<{ planNo: string; planName: string; patrolStandardId?: string; factoryCode: string; lineCode: string; enabled?: boolean }>({ planNo: '', planName: '', factoryCode: '', lineCode: '' })
const plantNow = () => new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date()).replace(' ', 'T')
const emptyDraft = (): PlanDraft => ({ planNo: '', planName: '', patrolStandardId: '', factoryCode: '', factoryName: '', lineCode: '', lineName: '', isEnabled: true, effectiveStartLocal: plantNow(), effectiveEndLocal: null, timeZoneId: 'America/New_York', schedule: { type: 'everyNHours', intervalHours: 2, times: null, daysOfWeek: null }, assigneeKey: null })
const draft = reactive<PlanDraft>(emptyDraft())
const times = ref<string[]>(['08:00'])
const weekdays = ref<string[]>(['Monday'])
const weekdayNames = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const weekdayOptions = computed(() => weekdayNames.map((value) => ({ value, label: t(`plan.day.${value}`) })))
const frequencyOptions = computed(() => [{ value: 'everyNHours', label: t('plan.everyNHours') }, { value: 'daily', label: t('plan.daily') }, { value: 'weekly', label: t('plan.weekly') }])
const statusOptions = computed(() => [{ value: true, label: t('common.enabled') }, { value: false, label: t('common.disabled') }])
const standardOptions = computed(() => standards.value.map((x) => ({ value: x.id, label: x.patrolStandardName })))
const assigneeOptions = computed(() => assignees.value.map((x) => ({ value: x.key, label: x.displayName })))
const pagination = computed(() => ({ current: page.value, pageSize: pageSize.value, total: total.value, showSizeChanger: true, onChange: changePage }))
const c = (key: string, field: string, width = 155) => ({ title: t(key), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [{ title: t('field.status'), key: 'isEnabled', width: 110 }, c('field.planNo', 'planNo'), c('field.planName', 'planName', 210), c('field.standard', 'patrolStandardName', 220), c('field.factory', 'factoryCode'), c('field.line', 'lineCode'), { title: t('field.schedule'), key: 'schedule', width: 215 }, c('field.effectiveStart', 'effectiveStartLocal', 170), c('field.effectiveEnd', 'effectiveEndLocal', 170), { title: t('field.assignedIpqa'), key: 'assigneeKey', width: 160 }, { title: t('field.updatedTime'), key: 'updatedAtUtc', width: 170 }, { title: t('common.actions'), key: 'actions', fixed: 'right', width: 235 }])

function scheduleText(schedule: ScheduleDefinition): string {
  if (schedule.type === 'everyNHours') return t('plan.everyHoursSummary', { count: schedule.intervalHours })
  const timeText = (schedule.times || []).join(', ')
  return schedule.type === 'daily' ? `${t('plan.daily')} · ${timeText}` : `${(schedule.daysOfWeek || []).map((day) => t(`plan.day.${day}`)).join(', ')} · ${timeText}`
}
function assigneeName(key: string | null) { return assignees.value.find((x) => x.key === key)?.displayName || key || '—' }
function plantDate(value: string) { return new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
async function load() { loading.value = true; error.value = ''; try { const data = await patrolPlansApi.list({ ...filters, page: page.value, pageSize: pageSize.value }); plans.value = data.items; total.value = data.total } catch { error.value = t('plan.apiUnavailable') } finally { loading.value = false } }
function search() { page.value = 1; void load() }
function reset() { Object.assign(filters, { planNo: '', planName: '', patrolStandardId: undefined, factoryCode: '', lineCode: '', enabled: undefined }); search() }
function changePage(next: number, size: number) { page.value = next; pageSize.value = size; void load() }
function openNew() { editingId.value = null; Object.assign(draft, emptyDraft()); times.value = ['08:00']; weekdays.value = ['Monday']; editorOpen.value = true }
async function openEdit(id: string) { busy.value = true; try { const row = await patrolPlansApi.get(id); editingId.value = id; Object.assign(draft, row); times.value = [...(row.schedule.times || ['08:00'])]; weekdays.value = [...(row.schedule.daysOfWeek || ['Monday'])]; editorOpen.value = true } catch { message.error(t('plan.loadFailed')) } finally { busy.value = false } }
function standardChanged(id: string) { const standard = standards.value.find((x) => x.id === id); if (standard) Object.assign(draft, { factoryCode: standard.factoryCode, factoryName: standard.factoryName, lineCode: standard.lineCode, lineName: standard.lineName }) }
function frequencyChanged() { times.value = ['08:00']; weekdays.value = ['Monday'] }
async function save() {
  const payload: PlanDraft = { ...draft, schedule: { type: draft.schedule.type, intervalHours: draft.schedule.type === 'everyNHours' ? draft.schedule.intervalHours : null, times: draft.schedule.type === 'everyNHours' ? null : [...times.value], daysOfWeek: draft.schedule.type === 'weekly' ? [...weekdays.value] : null } }
  const problem = validatePlanDraft(payload)
  if (problem) { message.error(t(problem)); return }
  busy.value = true
  try { if (editingId.value) await patrolPlansApi.update(editingId.value, payload); else await patrolPlansApi.create(payload); editorOpen.value = false; message.success(t('plan.saved')); await load() }
  catch (cause) { message.error(cause instanceof Error ? cause.message : t('plan.saveFailed')) }
  finally { busy.value = false }
}
async function toggle(row: Plan) { busy.value = true; try { await patrolPlansApi.setEnabled(row.id, !row.isEnabled); await load() } catch (cause) { message.error(cause instanceof Error ? cause.message : t('plan.saveFailed')) } finally { busy.value = false } }
async function remove(id: string) { busy.value = true; try { await patrolPlansApi.remove(id); await load() } catch (cause) { message.error(cause instanceof Error ? cause.message : t('plan.saveFailed')) } finally { busy.value = false } }
onMounted(async () => {
  let lookupFailed = false
  try { [standards.value, assignees.value] = await Promise.all([patrolPlansApi.standards(), patrolPlansApi.assignees()]) }
  catch { lookupFailed = true }
  await load()
  if (lookupFailed) error.value = t('plan.apiUnavailable')
})
</script>

<style scoped>
.plan-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.plan-time-row { display: flex; gap: 8px; margin-bottom: 8px; }
</style>
