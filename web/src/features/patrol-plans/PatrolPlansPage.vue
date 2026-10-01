<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.plans.title')" :subtitle="t('page.plans.subtitle')" />
    <div class="data-panel">
      <div class="filter-bar">
        <a-form layout="inline">
          <a-form-item :label="t('filter.planName')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.planName') })" /></a-form-item>
          <a-form-item :label="t('filter.factory')"><a-select allow-clear :placeholder="t('filter.select', { field: t('filter.factory') })" :options="factoryOptions" /></a-form-item>
          <a-form-item :label="t('filter.line')"><a-select allow-clear :placeholder="t('filter.select', { field: t('filter.line') })" :options="lineOptions" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary"><SearchOutlined />{{ t('common.search') }}</a-button><a-button><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar"><div><a-button type="primary"><PlusOutlined />{{ t('action.new') }}</a-button><a-button><CopyOutlined />{{ t('action.copy') }}</a-button></div></div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="plans" :scroll="{ x: 1450 }" :pagination="{ pageSize: 10, showSizeChanger: true }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'enabled'"><StatusTag :tone="record.enabled ? 'success' : 'neutral'" :label="record.enabled ? t('common.enabled') : t('common.disabled')" /></template>
          <template v-else-if="column.key === 'actions'"><a-button type="link" size="small">{{ t('common.edit') }}</a-button></template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnsType } from 'ant-design-vue'
import { CopyOutlined, PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { plans } from '@/services/mockData'

const { t } = useI18n()
const factoryOptions = [{ value: 'JAX-01', label: 'JAX-01 · Jacksonville Plant' }]
const lineOptions = ['LN-A', 'LN-B', 'LN-C'].map((value) => ({ value, label: value }))
const c = (key: string, field: string, width = 150) => ({ title: t(key), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  { title: t('field.status'), key: 'enabled', width: 110 }, c('field.effectiveStart', 'start'), c('field.effectiveEnd', 'end'),
  c('field.schedule', 'schedule', 170), c('field.planNo', 'no', 150), c('field.planName', 'name', 220), c('field.standard', 'standard', 260),
  c('field.factory', 'factory'), c('field.line', 'line'), c('field.assignedIpqa', 'assigned', 160), { title: t('common.actions'), key: 'actions', fixed: 'right', width: 90 },
])
</script>
