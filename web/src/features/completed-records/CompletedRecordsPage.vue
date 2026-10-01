<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.records.title')" :subtitle="t('page.records.subtitle')" />
    <div class="data-panel">
      <div class="filter-bar records-filter">
        <a-form layout="inline">
          <a-form-item :label="t('filter.taskNo')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.taskNo') })" /></a-form-item>
          <a-form-item :label="t('filter.completionTime')"><a-range-picker /></a-form-item>
          <a-form-item :label="t('filter.standardName')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.standardName') })" /></a-form-item>
          <a-form-item :label="t('filter.factory')"><a-select allow-clear :placeholder="t('filter.select', { field: t('filter.factory') })" :options="factoryOptions" /></a-form-item>
          <a-form-item :label="t('filter.line')"><a-select allow-clear :placeholder="t('filter.select', { field: t('filter.line') })" :options="lineOptions" /></a-form-item>
          <a-form-item :label="t('filter.inspector')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.inspector') })" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary"><SearchOutlined />{{ t('common.search') }}</a-button><a-button><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="records" :scroll="{ x: 1850 }" :pagination="{ pageSize: 10, showSizeChanger: true }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'result'"><StatusTag :tone="record.result === 'pass' ? 'success' : 'warning'" :label="t(`status.${record.result}`)" /></template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="showDetails(record)">{{ t('common.details') }}</a-button>
              <a-button type="link" size="small" @click="showPreview(record)">{{ t('action.preview') }}</a-button>
              <a-button type="link" size="small"><DownloadOutlined />{{ t('action.download') }}</a-button>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="detailOpen" :title="t('record.detailTitle')" width="min(1120px, 94vw)">
      <template v-if="selectedRecord">
        <h3 class="section-title">{{ t('record.basic') }}</h3>
        <a-descriptions bordered size="small" :column="3">
          <a-descriptions-item :label="t('field.taskNo')">{{ selectedRecord.taskNo }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.inspector')">{{ selectedRecord.inspector }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.shift')">{{ selectedRecord.shift }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.patrolPlan')">{{ selectedRecord.plan }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.standard')">{{ selectedRecord.standard }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.factory')">{{ selectedRecord.factory }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.line')">{{ selectedRecord.line }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.createdTime')">{{ selectedRecord.created }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.completionTime')">{{ selectedRecord.completed }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.approver')">{{ selectedRecord.approver }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.approvalTime')">{{ selectedRecord.approval }}</a-descriptions-item>
        </a-descriptions>
        <h3 class="section-title">{{ t('record.inspection') }}</h3>
        <a-table size="small" :columns="inspectionColumns" :data-source="[inspectionDetail]" :scroll="{ x: 2350 }" :pagination="false" bordered>
          <template #bodyCell="{ column }"><template v-if="column.key === 'referencePhoto' || column.key === 'inspectionPhoto'"><span class="photo-placeholder">{{ t('record.photoPlaceholder') }}</span></template></template>
        </a-table>
      </template>
    </a-drawer>

    <a-modal v-model:open="previewOpen" :title="t('record.previewTitle')" width="860px" :footer="null">
      <a-alert type="info" show-icon :message="t('record.previewNote')" />
      <article v-if="selectedRecord" class="report-preview">
        <header><div class="report-mark">JX</div><div><h2>{{ t('record.reportTitle') }}</h2><p>{{ t('record.reportStatus') }}</p></div><div><span>{{ t('record.documentNo') }}</span><strong>{{ selectedRecord.taskNo }}</strong></div></header>
        <a-descriptions bordered size="small" :column="2">
          <a-descriptions-item :label="t('field.standard')">{{ selectedRecord.standard }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.patrolPlan')">{{ selectedRecord.plan }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.factory')">{{ selectedRecord.factory }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.line')">{{ selectedRecord.line }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.inspector')">{{ selectedRecord.inspector }}</a-descriptions-item>
          <a-descriptions-item :label="t('field.approver')">{{ selectedRecord.approver }}</a-descriptions-item>
        </a-descriptions>
        <table class="report-table"><thead><tr><th>{{ t('field.inspectionItem') }}</th><th>{{ t('field.inspectionType') }}</th><th>{{ t('field.lowerLimit') }}</th><th>{{ t('field.upperLimit') }}</th><th>{{ t('field.result') }}</th></tr></thead><tbody><tr><td>{{ inspectionDetail.item }}</td><td>{{ inspectionDetail.type }}</td><td>{{ inspectionDetail.lower }}</td><td>{{ inspectionDetail.upper }}</td><td>{{ t('status.pass') }}</td></tr></tbody></table>
      </article>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnsType } from 'ant-design-vue'
import { DownloadOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { inspectionDetail, records } from '@/services/mockData'

type RecordRow = (typeof records)[number]
const { t } = useI18n()
const detailOpen = ref(false)
const previewOpen = ref(false)
const selectedRecord = ref<RecordRow>()
const factoryOptions = [{ value: 'JAX-01', label: 'JAX-01 · Jacksonville Plant' }]
const lineOptions = ['LN-A', 'LN-B', 'LN-C'].map((value) => ({ value, label: value }))
const c = (key: string, field: string, width = 150) => ({ title: t(key), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [c('field.taskNo', 'taskNo', 160), c('field.completionTime', 'completed', 175), { title: t('field.result'), key: 'result', width: 130 }, c('field.inspector', 'inspector'), c('field.standardName', 'standard', 250), c('field.planName', 'plan', 220), c('field.factory', 'factory'), c('field.line', 'line'), c('field.planNo', 'planNo', 160), c('field.approver', 'approver'), c('field.approvalTime', 'approval', 175), { title: t('common.actions'), key: 'actions', fixed: 'right', width: 330 }])
const inspectionColumns = computed<TableColumnsType>(() => [c('field.inspectionItem', 'item', 190), c('field.processInfo', 'process', 190), { title: t('field.referencePhoto'), key: 'referencePhoto', width: 160 }, { title: t('field.inspectionPhoto'), key: 'inspectionPhoto', width: 160 }, c('field.inspectionType', 'type'), c('field.upperLimit', 'upper'), c('field.lowerLimit', 'lower'), c('field.samplingPlan', 'sampling', 180), c('field.sampleCount', 'count'), c('field.inspectionDate', 'date'), c('field.inspectionTime', 'time'), c('field.result', 'result'), c('field.reinspectionResult', 'reinspection', 170), c('field.abnormalCode', 'abnormalCode'), c('field.abnormalType', 'abnormalType'), c('field.abnormalReason', 'abnormalReason'), c('field.abnormalDescription', 'abnormalDescription', 200), c('field.auditLog', 'audit', 240)])
function showDetails(record: RecordRow) { selectedRecord.value = record; detailOpen.value = true }
function showPreview(record: RecordRow) { selectedRecord.value = record; previewOpen.value = true }
</script>
