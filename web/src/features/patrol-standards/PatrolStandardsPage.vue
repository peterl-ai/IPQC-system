<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.standards.title')" :subtitle="t('page.standards.subtitle')" />

    <div class="data-panel">
      <div class="filter-bar">
        <a-form layout="inline">
          <a-form-item :label="t('filter.standardName')"><a-input v-model:value="filters.name" allow-clear :placeholder="t('filter.placeholder', { field: t('filter.standardName') })" /></a-form-item>
          <a-form-item :label="t('filter.factory')"><a-select v-model:value="filters.factory" allow-clear :placeholder="t('filter.select', { field: t('filter.factory') })" :options="factoryOptions" /></a-form-item>
          <a-form-item :label="t('filter.line')"><a-select v-model:value="filters.line" allow-clear :placeholder="t('filter.select', { field: t('filter.line') })" :options="lineOptions" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary"><SearchOutlined />{{ t('common.search') }}</a-button><a-button @click="resetFilters"><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar">
        <div>
          <a-button type="primary" @click="editorOpen = true"><PlusOutlined />{{ t('action.new') }}</a-button>
          <a-button><SaveOutlined />{{ t('action.save') }}</a-button>
          <a-button><CopyOutlined />{{ t('action.copy') }}</a-button>
        </div>
        <div>
          <a-button><ExportOutlined />{{ t('action.export') }}</a-button>
          <a-button><ImportOutlined />{{ t('action.import') }}</a-button>
          <a-button><DownloadOutlined />{{ t('action.template') }}</a-button>
        </div>
      </div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="standards" :scroll="{ x: 1900 }" :pagination="pagination">
        <template #bodyCell="{ column }">
          <template v-if="column.key === 'actions'"><a-button type="link" size="small" @click="editorOpen = true">{{ t('common.edit') }}</a-button></template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="editorOpen" :title="t('editor.title')" width="min(1080px, 92vw)" class="editor-drawer">
      <a-alert type="info" show-icon :message="t('editor.nonPersistent')" />
      <h3 class="section-title">{{ t('editor.header') }}</h3>
      <a-form layout="vertical" class="editor-form">
        <a-form-item :label="t('field.factoryCode')"><a-input /></a-form-item>
        <a-form-item :label="t('field.factoryName')"><a-input /></a-form-item>
        <a-form-item :label="t('field.workshopCode')"><a-input /></a-form-item>
        <a-form-item :label="t('field.lineCode')"><a-input /></a-form-item>
        <a-form-item :label="t('field.lineName')" required><a-input /></a-form-item>
        <a-form-item :label="t('field.standardName')" required><a-input /></a-form-item>
        <a-form-item :label="t('field.materialCode')"><a-input /></a-form-item>
      </a-form>
      <div class="section-title-row"><h3 class="section-title">{{ t('editor.detail') }}</h3><a-button><PlusOutlined />{{ t('editor.addItem') }}</a-button></div>
      <a-table size="small" :columns="detailColumns" :data-source="detailRows" :scroll="{ x: 2100 }" :pagination="false" bordered />
      <template #footer><a-space><a-button type="primary">{{ t('action.save') }}</a-button><a-button @click="editorOpen = false">{{ t('common.cancel') }}</a-button></a-space></template>
    </a-drawer>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnsType } from 'ant-design-vue'
import { CopyOutlined, DownloadOutlined, ExportOutlined, ImportOutlined, PlusOutlined, ReloadOutlined, SaveOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import { standards } from '@/services/mockData'

const { t } = useI18n()
const editorOpen = ref(false)
const filters = reactive({ name: '', factory: undefined as string | undefined, line: undefined as string | undefined })
const factoryOptions = [{ value: 'JAX-01', label: 'JAX-01 · Jacksonville Plant' }]
const lineOptions = ['LN-A', 'LN-B', 'LN-C'].map((value) => ({ value, label: value }))
const pagination = computed(() => ({ pageSize: 10, showSizeChanger: true, showTotal: (total: number) => `${total}` }))
const column = (title: string, dataIndex: string, width = 140) => ({ title: t(title), dataIndex, key: dataIndex, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  column('field.no', 'no', 70), column('field.standardName', 'name', 230), column('field.factoryCode', 'factoryCode'),
  column('field.factoryName', 'factoryName', 180), column('field.workshopCode', 'workshopCode'), column('field.lineCode', 'lineCode'),
  column('field.lineName', 'lineName', 170), column('field.materialCode', 'materialCode'), column('field.createdBy', 'createdBy'),
  column('field.createdTime', 'createdTime', 170), column('field.updatedBy', 'updatedBy'), column('field.updatedTime', 'updatedTime', 170),
  { title: t('common.actions'), key: 'actions', fixed: 'right', width: 90 },
])
const detailColumns = computed<TableColumnsType>(() => [
  column('field.processCode', 'processCode'), column('field.processName', 'processName'), column('field.itemCategory', 'category', 180),
  column('field.inspectionItem', 'item', 180), column('field.inspectionContent', 'content', 220), column('field.upperOperator', 'upperOperator'),
  column('field.upperValue', 'upperValue'), column('field.lowerOperator', 'lowerOperator'), column('field.lowerValue', 'lowerValue'),
  column('field.inspectionType', 'type'), column('field.samplingPlan', 'sampling', 170), column('field.sampleCount', 'sampleCount'),
  column('field.photoRequirement', 'photo', 170), column('field.defectLevel', 'defect'),
])
const detailRows = [{ id: 'skeleton-1', processCode: 'LM-02', processName: 'Lamination', category: 'Process parameter', item: 'Temperature', content: 'Verify actual cycle temperature', upperOperator: '≤', upperValue: '155', lowerOperator: '≥', lowerValue: '145', type: 'Numeric', sampling: '5 modules / shift', sampleCount: '5', photo: 'On abnormal', defect: 'Major' }]
function resetFilters() { filters.name = ''; filters.factory = undefined; filters.line = undefined }
</script>
