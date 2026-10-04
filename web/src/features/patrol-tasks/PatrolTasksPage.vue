<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.tasks.title')" :subtitle="t('page.tasks.subtitle')" />
    <div class="data-panel">
      <a-alert v-if="error" type="error" show-icon :message="error" />
      <div class="filter-bar">
        <a-form layout="inline" @finish="search">
          <a-form-item :label="t('field.taskNo')"><a-input v-model:value="filters.taskNo" allow-clear /></a-form-item>
          <a-form-item :label="t('field.status')"><a-select v-model:value="filters.status" allow-clear :options="statusOptions" style="width: 180px" /></a-form-item>
          <a-form-item :label="t('field.planName')"><a-select v-model:value="filters.planId" allow-clear show-search option-filter-prop="label" :options="planOptions" style="width: 190px" /></a-form-item>
          <a-form-item :label="t('field.standard')"><a-select v-model:value="filters.standardId" allow-clear show-search option-filter-prop="label" :options="standardOptions" style="width: 190px" /></a-form-item>
          <a-form-item :label="t('field.factory')"><a-input v-model:value="filters.factory" allow-clear /></a-form-item>
          <a-form-item :label="t('field.line')"><a-input v-model:value="filters.line" allow-clear /></a-form-item>
          <a-form-item :label="t('field.inspector')"><a-input v-model:value="filters.inspector" allow-clear /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary" @click="search"><SearchOutlined />{{ t('common.search') }}</a-button><a-button @click="reset"><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar">
        <a-popconfirm :title="t('task.batchConfirm')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @confirm="batchApprove">
          <a-button type="primary" :disabled="selectedIds.length === 0 || busy">{{ t('task.approveSelected') }} ({{ selectedIds.length }})</a-button>
        </a-popconfirm>
      </div>
      <a-table
        row-key="id" size="small" :columns="columns" :data-source="tasks" :loading="loading"
        :scroll="{ x: 1800 }" :pagination="pagination"
        :row-selection="{ selectedRowKeys: selectedIds, onChange: onSelectChange, getCheckboxProps: checkboxProps }"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'status'"><StatusTag :tone="record.status === 'Completed' ? 'success' : record.status === 'Rejected' ? 'warning' : 'info'" :label="statusLabel(record.status)" /></template>
          <template v-else-if="column.key === 'overallInspectionResult'">{{ resultLabel(record.overallInspectionResult) }}</template>
          <template v-else-if="column.key === 'scheduledOccurrenceUtc'">{{ plantDate(record.scheduledOccurrenceUtc) }}</template>
          <template v-else-if="column.key === 'submittedAtUtc'">{{ plantDate(record.submittedAtUtc) }}</template>
          <template v-else-if="column.key === 'shift'">{{ shiftLabel(record.shift) }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openDetail(record.id)">{{ t('common.details') }}</a-button>
              <template v-if="record.status === 'PendingApproval'">
                <a-popconfirm :title="t('task.approveConfirm')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @confirm="approve(record.id)"><a-button type="link" size="small">{{ t('action.approve') }}</a-button></a-popconfirm>
                <a-button type="link" danger size="small" @click="openReject(record)">{{ t('action.reject') }}</a-button>
              </template>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="detailOpen" :title="t('task.detailTitle')" width="min(920px, 95vw)">
      <a-spin :spinning="detailLoading">
        <template v-if="detail">
          <h3 class="section-title">{{ t('record.basic') }}</h3>
          <a-descriptions bordered size="small" :column="2">
            <a-descriptions-item :label="t('field.taskNo')">{{ detail.summary.taskNo }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.status')">{{ statusLabel(detail.summary.status) }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.inspector')">{{ detail.summary.assignedInspectorKey }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.shift')">{{ shiftLabel(detail.summary.shift) }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.planNo')">{{ detail.summary.planNoSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.planName')">{{ detail.summary.planNameSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.standardName')">{{ detail.summary.standardNameSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.factory')">{{ detail.summary.factoryNameSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.workshopCode')">{{ detail.workshopCodeSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.line')">{{ detail.summary.lineNameSnapshot }}</a-descriptions-item>
            <a-descriptions-item :label="t('task.scheduledTime')">{{ plantDate(detail.summary.scheduledOccurrenceUtc) }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.submittedTime')">{{ plantDate(detail.summary.submittedAtUtc) }}</a-descriptions-item>
            <a-descriptions-item :label="t('field.result')">{{ resultLabel(detail.summary.overallInspectionResult) }}</a-descriptions-item>
          </a-descriptions>
          <h3 class="section-title">{{ t('task.inspectionItems') }}</h3>
          <a-empty v-if="detail.items.length === 0" :description="t('task.noSnapshot')" />
          <a-collapse v-else>
            <a-collapse-panel v-for="item in detail.items" :key="item.id" :header="`${item.sequenceNo}. ${item.inspectionItem}`">
              <h4>{{ t('task.itemDetails') }}</h4>
              <a-descriptions bordered size="small" :column="2">
                <a-descriptions-item :label="t('field.processInfo')">{{ item.processCode }} {{ item.processName }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.inspectionContent')">{{ item.inspectionContent }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.inspectionType')">{{ typeLabel(item.inspectionType) }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.lowerLimit')">{{ item.lowerLimitOperator }} {{ item.lowerLimitValue }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.upperLimit')">{{ item.upperLimitOperator }} {{ item.upperLimitValue }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.samplingPlan')">{{ item.samplingPlan }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.sampleCount')">{{ item.sampleCount }}</a-descriptions-item>
                <a-descriptions-item :label="t('task.isNa')">{{ naLabel(item.isNa) }}</a-descriptions-item>
                <a-descriptions-item :label="t('task.judgment')">{{ resultLabel(item.judgmentResult) }}</a-descriptions-item>
                <a-descriptions-item :label="t('field.inspectionTime')">{{ plantDate(item.samples[0]?.inspectedAtUtc) }}</a-descriptions-item>
                <a-descriptions-item :label="t('task.remarks')">{{ item.remarks }}</a-descriptions-item>
              </a-descriptions>
              <div v-for="sample in item.samples" :key="sample.id" class="sample-line">{{ t('task.sample') }} {{ sample.sequenceNo }} · {{ sample.inspectionValue ?? '—' }} · {{ resultLabel(sample.judgmentResult) }}</div>
            </a-collapse-panel>
          </a-collapse>
          <h3 class="section-title">{{ t('task.revisionHistory') }}</h3>
          <a-empty v-if="detail.submissions.length === 0" :description="t('task.noSubmissions')" />
          <a-timeline v-else>
            <a-timeline-item v-for="submission in detail.submissions" :key="submission.revisionNo">
              <strong>{{ t('task.revision') }} {{ submission.revisionNo }}</strong> · {{ submission.submittedBy }} · {{ plantDate(submission.submittedAtUtc) }} · {{ resultLabel(submission.overallInspectionResult) }}
              <div v-for="item in submission.items" :key="item.patrolTaskItemId" class="history-item">
                {{ item.sequenceNo }}. {{ itemName(item.patrolTaskItemId) }} · {{ resultLabel(item.judgmentResult) }}
                <span v-for="sample in item.samples" :key="sample.sequenceNo"> · {{ t('task.sample') }} {{ sample.sequenceNo }}: {{ sample.inspectionValue ?? '—' }} / {{ resultLabel(sample.judgmentResult) }}</span>
              </div>
              <div v-if="submission.review" class="review-line">{{ reviewLabel(submission.review.decision) }} · {{ submission.review.reviewer }} · {{ plantDate(submission.review.reviewedAtUtc) }}<span v-if="submission.review.reason"> · {{ t('field.rejectReason') }}: {{ submission.review.reason }}</span></div>
            </a-timeline-item>
          </a-timeline>
        </template>
      </a-spin>
    </a-drawer>

    <a-modal v-model:open="rejectOpen" :title="t('task.rejectTitle')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" :confirm-loading="busy" @ok="confirmReject">
      <p>{{ t('task.rejectHelp') }}</p>
      <a-form layout="vertical">
        <a-form-item :label="t('field.taskNo')"><a-input :value="rejectTask?.taskNo" disabled /></a-form-item>
        <a-form-item :label="t('field.rejectReason')" required :validate-status="reasonError ? 'error' : undefined" :help="reasonError ? t('task.reasonRequired') : undefined">
          <a-textarea v-model:value="reason" :rows="4" :placeholder="t('task.reasonPlaceholder')" />
        </a-form-item>
      </a-form>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { message, type TableColumnsType } from 'ant-design-vue'
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { patrolPlansApi } from '@/features/patrol-plans/api'
import { patrolTasksApi, TaskApiError, canSelectForApproval, validRejectReason, type TaskDetail, type TaskFilters, type TaskSummary } from './api'

const { t } = useI18n()
const filters = reactive<Omit<TaskFilters, 'page' | 'pageSize'>>({ status: 'Active' })
const tasks = ref<TaskSummary[]>([])
const planOptions = ref<{ value: string; label: string }[]>([])
const standardOptions = ref<{ value: string; label: string }[]>([])
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const selectedIds = ref<string[]>([])
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<TaskDetail | null>(null)
const rejectOpen = ref(false)
const rejectTask = ref<TaskSummary | null>(null)
const reason = ref('')
const reasonError = ref(false)
const statusOptions = computed(() => [
  { value: 'Active', label: t('task.active') },
  ...(['PendingInspection', 'InProgress', 'PendingApproval', 'Rejected', 'Completed'] as const).map((value) => ({ value, label: statusLabel(value) })),
])
const pagination = computed(() => ({ current: page.value, pageSize: pageSize.value, total: total.value, showSizeChanger: true, onChange: changePage }))
const c = (key: string, field: string, width = 150) => ({ title: t(key), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  { title: t('field.status'), key: 'status', width: 170 }, c('field.taskNo', 'taskNo', 210),
  { title: t('task.scheduledTime'), key: 'scheduledOccurrenceUtc', width: 180 },
  { title: t('field.submittedTime'), key: 'submittedAtUtc', width: 180 },
  { title: t('field.result'), key: 'overallInspectionResult', width: 150 },
  c('field.inspector', 'assignedInspectorKey', 150), c('field.planName', 'planNameSnapshot', 190),
  c('field.standardName', 'standardNameSnapshot', 200), c('field.factory', 'factoryNameSnapshot'),
  c('field.line', 'lineNameSnapshot'), { title: t('field.shift'), key: 'shift', width: 100 },
  { title: t('common.actions'), key: 'actions', fixed: 'right', width: 240 },
])
function plantDate(value?: string | null) { return value ? new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : '—' }
function statusLabel(value: string) { return t(`task.status.${value}`) }
function resultLabel(value?: string | null) { return value ? t(`task.result.${value}`) : '—' }
function shiftLabel(value?: string | null) { return value ? t(`task.shift.${value}`) : '—' }
function typeLabel(value: string) { return t(`task.type.${value}`) }
function naLabel(value: boolean | null) { return value === null ? '—' : t(value ? 'task.yes' : 'task.no') }
function reviewLabel(value: string) { return t(`task.review.${value}`) }
function itemName(id: string) { return detail.value?.items.find((x) => x.id === id)?.inspectionItem || id }
function onSelectChange(keys: (string | number)[]) { selectedIds.value = keys.map(String) }
function checkboxProps(record: TaskSummary) { return { disabled: !canSelectForApproval(record) } }
async function load() {
  loading.value = true; error.value = ''
  try { const data = await patrolTasksApi.list({ ...filters, page: page.value, pageSize: pageSize.value }); tasks.value = data.items; total.value = data.total; selectedIds.value = [] }
  catch (cause) { error.value = apiError(cause, 'task.loadFailed') }
  finally { loading.value = false }
}
function search() { page.value = 1; void load() }
function reset() { Object.assign(filters, { taskNo: undefined, status: 'Active', planId: undefined, standardId: undefined, factory: undefined, line: undefined, inspector: undefined }); search() }
function changePage(next: number, size: number) { page.value = next; pageSize.value = size; void load() }
function apiError(cause: unknown, fallback: string) { return cause instanceof TaskApiError && cause.status === 409 ? t('task.conflict') : t(fallback) }
async function openDetail(id: string) {
  detailOpen.value = true; detailLoading.value = true; detail.value = null
  try { detail.value = await patrolTasksApi.get(id) }
  catch (cause) { message.error(apiError(cause, 'task.loadFailed')) }
  finally { detailLoading.value = false }
}
async function approve(id: string) {
  busy.value = true
  try { await patrolTasksApi.approve(id); message.success(t('task.approved')); await load(); if (detail.value?.summary.id === id) await openDetail(id) }
  catch (cause) { message.error(apiError(cause, 'task.reviewFailed')); await load() }
  finally { busy.value = false }
}
async function batchApprove() {
  if (!selectedIds.value.length) return
  busy.value = true
  try { await patrolTasksApi.batchApprove(selectedIds.value); message.success(t('task.approved')); await load() }
  catch (cause) { message.error(apiError(cause, 'task.reviewFailed')); await load() }
  finally { busy.value = false }
}
function openReject(task: TaskSummary) { rejectTask.value = task; reason.value = ''; reasonError.value = false; rejectOpen.value = true }
async function confirmReject() {
  if (!validRejectReason(reason.value)) { reasonError.value = true; return }
  if (!rejectTask.value) return
  busy.value = true
  try { await patrolTasksApi.reject(rejectTask.value.id, reason.value.trim()); rejectOpen.value = false; message.success(t('task.rejected')); await load() }
  catch (cause) { message.error(apiError(cause, 'task.reviewFailed')); await load() }
  finally { busy.value = false }
}
async function loadOptions() {
  try {
    const standards = await patrolPlansApi.standards()
    standardOptions.value = standards.map((x) => ({ value: x.id, label: x.patrolStandardName }))
    const plans: { id: string; planName: string }[] = []
    for (let index = 1; ; index++) {
      const data = await patrolPlansApi.list({ page: index, pageSize: 100 })
      plans.push(...data.items)
      if (plans.length >= data.total || data.items.length === 0) break
    }
    planOptions.value = plans.map((x) => ({ value: x.id, label: x.planName }))
  } catch { /* The Task list remains usable when lookup options are unavailable. */ }
}
onMounted(() => { void load(); void loadOptions() })
</script>

<style scoped>
.sample-line, .history-item, .review-line { margin-top: 8px; }
.history-item { color: #4b5563; }
.review-line { font-weight: 600; }
</style>
