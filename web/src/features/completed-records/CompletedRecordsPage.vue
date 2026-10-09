<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.records.title')" :subtitle="t('page.records.subtitle')" />
    <div class="data-panel">
      <div class="filter-bar records-filter">
        <a-form layout="inline">
          <a-form-item v-for="field in textFilters" :key="field" :label="t(fieldLabels[field])">
            <a-input v-model:value="filters[field]" allow-clear :placeholder="t('filter.placeholder', { field: t(fieldLabels[field]) })" />
          </a-form-item>
          <a-form-item :label="t('field.shift')"><a-select v-model:value="filters.shift" allow-clear style="width: 130px" :options="shiftOptions" /></a-form-item>
          <a-form-item :label="t('field.result')"><a-select v-model:value="filters.result" allow-clear style="width: 150px" :options="resultOptions" /></a-form-item>
          <a-form-item :label="t('record.completedFrom')"><a-input v-model:value="filters.completedFrom" type="date" /></a-form-item>
          <a-form-item :label="t('record.completedTo')"><a-input v-model:value="filters.completedTo" type="date" /></a-form-item>
        </a-form>
        <div class="filter-actions">
          <a-button type="primary" @click="search"><SearchOutlined />{{ t('common.search') }}</a-button>
          <a-button @click="reset"><ReloadOutlined />{{ t('common.reset') }}</a-button>
        </div>
      </div>
      <a-alert v-if="error" type="error" show-icon :message="error" class="record-alert" />
      <a-table row-key="id" size="small" :columns="columns" :data-source="rows" :loading="loading" :scroll="{ x: 1750 }" :pagination="pagination">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'completedAtUtc'">{{ plantDate(record.completedAtUtc) }}</template>
          <template v-else-if="column.key === 'approvedAtUtc'">{{ plantDate(record.approvedAtUtc) }}</template>
          <template v-else-if="column.key === 'overallInspectionResult'"><StatusTag :tone="record.overallInspectionResult === 'Qualified' ? 'success' : 'warning'" :label="resultLabel(record.overallInspectionResult)" /></template>
          <template v-else-if="column.key === 'shift'">{{ shiftLabel(record.shift) }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openDetail(record.id)">{{ t('common.details') }}</a-button>
              <a-button type="link" size="small" @click="openPreview(record.id)">{{ t('action.preview') }}</a-button>
              <a-button type="link" size="small" @click="download(record)">{{ t('action.download') }}</a-button>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="detailOpen" :title="t('record.detailTitle')" width="min(1120px, 94vw)">
      <a-spin :spinning="detailLoading">
        <template v-if="detail">
          <h3 class="section-title">{{ t('record.basic') }}</h3>
          <a-descriptions bordered size="small" :column="2">
            <a-descriptions-item v-for="entry in detailFields(detail)" :key="entry.label" :label="t(entry.label)">{{ entry.value }}</a-descriptions-item>
          </a-descriptions>
          <h3 class="section-title">{{ t('record.inspection') }}</h3>
          <a-card v-for="item in detail.items" :key="item.sequenceNo" size="small" class="record-card" :title="`${item.sequenceNo}. ${item.inspectionItem}`">
            <a-descriptions size="small" :column="2">
              <a-descriptions-item :label="t('field.processInfo')">{{ item.processCode }} {{ item.processName }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.inspectionContent')">{{ item.inspectionContent }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.itemCategory')">{{ item.inspectionItemCategory }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.inspectionType')">{{ typeLabel(item.inspectionType) }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.lowerLimit')">{{ item.lowerLimitOperator }} {{ item.lowerLimitValue }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.upperLimit')">{{ item.upperLimitOperator }} {{ item.upperLimitValue }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.samplingPlan')">{{ item.samplingPlan }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.sampleCount')">{{ item.sampleCount }}</a-descriptions-item>
              <a-descriptions-item :label="t('task.isNa')">{{ t(item.isNa ? 'task.yes' : 'task.no') }}</a-descriptions-item>
              <a-descriptions-item :label="t('task.judgment')">{{ resultLabel(item.judgmentResult) }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.inspectionTime')">{{ plantDate(item.inspectedAtUtc) }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.photoRequirement')">{{ item.photoRequirement }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.defectLevel')">{{ item.defectLevel }}</a-descriptions-item>
              <a-descriptions-item :label="t('record.machineCode')">{{ item.machineCode || '—' }}</a-descriptions-item>
              <a-descriptions-item :label="t('record.series')">{{ item.series || '—' }}</a-descriptions-item>
              <a-descriptions-item :label="t('record.mold')">{{ item.mold || '—' }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.abnormalType')">{{ item.abnormalType || '—' }}</a-descriptions-item>
              <a-descriptions-item :label="t('field.abnormalReason')">{{ item.abnormalCause || '—' }}</a-descriptions-item>
              <a-descriptions-item :label="t('task.remarks')">{{ item.remarks || '—' }}</a-descriptions-item>
            </a-descriptions>
            <div v-for="sample in item.samples" :key="sample.sequenceNo" class="sample-line">
              {{ t('task.sample') }} {{ sample.sequenceNo }} · {{ sample.inspectionValue ?? resultLabel(sample.judgmentResult) }} · {{ resultLabel(sample.judgmentResult) }} · {{ plantDate(sample.inspectedAtUtc) }}
            </div>
          </a-card>
          <h3 class="section-title">{{ t('record.finalApproval') }}</h3>
          <p>{{ detail.summary.approvedBy }} · {{ plantDate(detail.summary.approvedAtUtc) }}</p>
          <h3 class="section-title">{{ t('task.revisionHistory') }}</h3>
          <a-timeline>
            <a-timeline-item v-for="revision in detail.revisionHistory" :key="revision.revisionNo">
              <strong>{{ t('task.revision') }} {{ revision.revisionNo }} · {{ resultLabel(revision.overallInspectionResult) }} · {{ reviewLabel(revision.reviewDecision) }}</strong>
              <div>{{ revision.submittedBy }} · {{ plantDate(revision.submittedAtUtc) }}</div>
              <div>{{ revision.reviewedBy }} · {{ plantDate(revision.reviewedAtUtc) }}</div>
              <div v-if="revision.rejectReason">{{ t('field.rejectReason') }}: {{ revision.rejectReason }}</div>
              <div v-for="item in revision.items" :key="item.sequenceNo">
                {{ t('field.inspectionItem') }} {{ item.sequenceNo }}: {{ resultLabel(item.judgmentResult) }} · {{ plantDate(item.inspectedAtUtc) }}
                <span v-for="sample in item.samples" :key="sample.sequenceNo"> · {{ t('task.sample') }} {{ sample.sequenceNo }}: {{ sample.inspectionValue ?? resultLabel(sample.judgmentResult) }}</span>
              </div>
            </a-timeline-item>
          </a-timeline>
        </template>
      </a-spin>
    </a-drawer>

    <a-modal v-model:open="previewOpen" :title="t('record.previewTitle')" width="min(1100px, 96vw)" :footer="null">
      <a-spin :spinning="previewLoading">
        <article v-if="preview" class="report-preview">
          <header><h2>{{ t('record.reportTitle') }}</h2><strong>{{ preview.summary.taskNo }}</strong></header>
          <a-descriptions bordered size="small" :column="2">
            <a-descriptions-item v-for="entry in summaryFields(preview.summary)" :key="entry.label" :label="t(entry.label)">{{ entry.value }}</a-descriptions-item>
          </a-descriptions>
          <h3>{{ t('record.inspection') }}</h3>
          <table class="report-table">
            <thead><tr><th>{{ t('field.inspectionItem') }}</th><th>{{ t('field.inspectionContent') }}</th><th>{{ t('field.inspectionType') }}</th><th>{{ t('field.lowerLimit') }}</th><th>{{ t('field.upperLimit') }}</th><th>{{ t('task.isNa') }}</th><th>{{ t('field.result') }}</th><th>{{ t('field.inspectionTime') }}</th><th>{{ t('task.sample') }}</th></tr></thead><tbody>
              <template v-for="item in preview.items" :key="item.sequenceNo">
                <tr v-if="item.samples.length === 0"><td>{{ item.inspectionItem }}</td><td>{{ item.inspectionContent }}</td><td>{{ typeLabel(item.inspectionType) }}</td><td>{{ item.lowerLimitOperator }} {{ item.lowerLimitValue }}</td><td>{{ item.upperLimitOperator }} {{ item.upperLimitValue }}</td><td>{{ t(item.isNa ? 'task.yes' : 'task.no') }}</td><td>{{ resultLabel(item.judgmentResult) }}</td><td>{{ plantDate(item.inspectedAtUtc) }}</td><td>—</td></tr>
                <tr v-for="sample in item.samples" v-else :key="sample.sequenceNo"><td>{{ item.inspectionItem }}</td><td>{{ item.inspectionContent }}</td><td>{{ typeLabel(item.inspectionType) }}</td><td>{{ item.lowerLimitOperator }} {{ item.lowerLimitValue }}</td><td>{{ item.upperLimitOperator }} {{ item.upperLimitValue }}</td><td>{{ t(item.isNa ? 'task.yes' : 'task.no') }}</td><td>{{ resultLabel(item.judgmentResult) }}</td><td>{{ plantDate(item.inspectedAtUtc) }}</td><td>{{ sample.sequenceNo }}: {{ sample.inspectionValue ?? resultLabel(sample.judgmentResult) }} · {{ resultLabel(sample.judgmentResult) }} · {{ plantDate(sample.inspectedAtUtc) }}</td></tr>
              </template>
            </tbody>
          </table>
          <p>{{ t('record.finalApproval') }}: {{ preview.summary.approvedBy }} · {{ plantDate(preview.summary.approvedAtUtc) }}</p>
        </article>
      </a-spin>
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
import { completedRecordsApi, CompletedRecordApiError, type CompletedPatrolReport, type CompletedRecordSummary, type RecordFilters } from './api'

const { t } = useI18n()
const filters = reactive<Omit<RecordFilters, 'page' | 'pageSize'>>({})
const textFilters = ['taskNo', 'plan', 'standard', 'factory', 'line', 'inspector'] as const
const fieldLabels: Record<(typeof textFilters)[number], string> = {
  taskNo: 'filter.taskNo', plan: 'filter.planName', standard: 'filter.standardName',
  factory: 'filter.factory', line: 'filter.line', inspector: 'filter.inspector',
}
const rows = ref<CompletedRecordSummary[]>([])
const page = ref(1); const pageSize = ref(10); const total = ref(0)
const loading = ref(false); const error = ref('')
const detailOpen = ref(false); const detailLoading = ref(false); const detail = ref<CompletedPatrolReport | null>(null)
const previewOpen = ref(false); const previewLoading = ref(false); const preview = ref<CompletedPatrolReport | null>(null)
const shiftOptions = computed(() => ['Day', 'Night'].map(value => ({ value, label: shiftLabel(value) })))
const resultOptions = computed(() => ['Qualified', 'Unqualified'].map(value => ({ value, label: resultLabel(value) })))
const pagination = computed(() => ({ current: page.value, pageSize: pageSize.value, total: total.value, showSizeChanger: true, onChange: changePage }))
const c = (label: string, field: string, width = 150) => ({ title: t(label), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  c('field.taskNo', 'taskNo', 210), c('field.completionTime', 'completedAtUtc', 190),
  { title: t('field.result'), key: 'overallInspectionResult', width: 145 }, c('field.inspector', 'inspector'),
  { title: t('field.shift'), key: 'shift', width: 90 }, c('field.planName', 'planName', 180),
  c('field.standardName', 'standardName', 200), c('field.factory', 'factoryName'),
  c('field.line', 'lineName'), c('field.materialCode', 'materialCode'),
  c('field.approver', 'approvedBy'), c('field.approvalTime', 'approvedAtUtc', 190),
  { title: t('common.actions'), key: 'actions', fixed: 'right', width: 300 },
])
function plantDate(value?: string | null) {
  return value ? new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }).format(new Date(value)) : '—'
}
function resultLabel(value?: string | null) { return value ? t(`task.result.${value}`) : '—' }
function shiftLabel(value?: string | null) { return value ? t(`task.shift.${value}`) : '—' }
function reviewLabel(value?: string | null) { return value ? t(`task.review.${value}`) : '—' }
function typeLabel(value: string) { return t(`task.type.${value}`) }
function summaryFields(row: CompletedRecordSummary) { return [
  { label: 'field.taskNo', value: row.taskNo }, { label: 'field.planNo', value: row.planNo },
  { label: 'field.planName', value: row.planName }, { label: 'field.standardName', value: row.standardName },
  { label: 'field.factoryCode', value: row.factoryCode }, { label: 'field.factory', value: row.factoryName },
  { label: 'field.workshopCode', value: row.workshopCode }, { label: 'field.lineCode', value: row.lineCode },
  { label: 'field.line', value: row.lineName }, { label: 'field.materialCode', value: row.materialCode },
  { label: 'field.inspector', value: row.inspector }, { label: 'field.shift', value: shiftLabel(row.shift) },
  { label: 'task.scheduledTime', value: plantDate(row.scheduledOccurrenceUtc) },
  { label: 'field.submittedTime', value: plantDate(row.submittedAtUtc) },
  { label: 'field.completionTime', value: plantDate(row.completedAtUtc) },
  { label: 'task.revision', value: row.finalRevisionNo },
  { label: 'field.result', value: resultLabel(row.overallInspectionResult) },
  { label: 'field.approver', value: row.approvedBy },
  { label: 'field.approvalTime', value: plantDate(row.approvedAtUtc) },
] }
function detailFields(report: CompletedPatrolReport) { return [
  ...summaryFields(report.summary),
  { label: 'field.generatedTime', value: plantDate(report.generatedAtUtc) },
  { label: 'record.startedTime', value: plantDate(report.startedAtUtc) },
] }
function apiError(cause: unknown) { return cause instanceof CompletedRecordApiError && cause.status === 409 ? t('record.integrityConflict') : t('record.loadFailed') }
async function load() {
  loading.value = true; error.value = ''
  try { const data = await completedRecordsApi.list({ ...filters, page: page.value, pageSize: pageSize.value }); rows.value = data.items; total.value = data.total }
  catch (cause) { error.value = apiError(cause) }
  finally { loading.value = false }
}
function search() { page.value = 1; void load() }
function reset() { for (const key of [...textFilters, 'shift', 'result', 'completedFrom', 'completedTo'] as const) filters[key] = undefined; search() }
function changePage(next: number, size: number) { page.value = next; pageSize.value = size; void load() }
async function openDetail(id: string) {
  detailOpen.value = true; detailLoading.value = true; detail.value = null
  try { detail.value = await completedRecordsApi.get(id) } catch (cause) { message.error(apiError(cause)) }
  finally { detailLoading.value = false }
}
async function openPreview(id: string) {
  previewOpen.value = true; previewLoading.value = true; preview.value = null
  try { preview.value = await completedRecordsApi.report(id) } catch (cause) { message.error(apiError(cause)) }
  finally { previewLoading.value = false }
}
async function download(row: CompletedRecordSummary) {
  try {
    const blob = await completedRecordsApi.download(row.id)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a'); anchor.href = url
    anchor.download = `IPQC-${row.taskNo.replace(/[^A-Za-z0-9_-]/g, '')}.xlsx`
    document.body.appendChild(anchor); anchor.click(); anchor.remove(); URL.revokeObjectURL(url)
  } catch (cause) { message.error(apiError(cause)) }
}
onMounted(() => { void load() })
</script>

<style scoped>
.record-alert { margin: 12px 0; }
.record-card { margin: 12px 0; }
.sample-line { margin: 6px 0; color: #4b5563; }
.report-preview { overflow-x: auto; }
</style>
