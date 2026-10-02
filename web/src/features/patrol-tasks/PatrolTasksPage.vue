<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.tasks.title')" :subtitle="t('page.tasks.subtitle')" />
    <div class="data-panel">
      <div class="filter-bar">
        <a-form layout="inline">
          <a-form-item :label="t('filter.taskNo')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.taskNo') })" /></a-form-item>
          <a-form-item :label="t('filter.status')"><a-select allow-clear :placeholder="t('filter.select', { field: t('filter.status') })" :options="statusOptions" /></a-form-item>
          <a-form-item :label="t('filter.inspector')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.inspector') })" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary"><SearchOutlined />{{ t('common.search') }}</a-button><a-button><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="tasks" :scroll="{ x: 1600 }" :pagination="{ pageSize: 10, showSizeChanger: true }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'status'"><StatusTag tone="info" :label="t(`status.${record.status}`)" /></template>
          <template v-else-if="column.key === 'result'"><StatusTag :tone="record.result === 'pass' ? 'success' : 'warning'" :label="t(`status.${record.result}`)" /></template>
          <template v-else-if="column.key === 'actions'">
            <a-space><a-button type="link" size="small">{{ t('action.approve') }}</a-button><a-button type="link" danger size="small" @click="openReject(record.no)">{{ t('action.reject') }}</a-button></a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>
    <a-modal v-model:open="rejectOpen" :title="t('task.rejectTitle')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @ok="confirmReject">
      <p class="modal-help">{{ t('task.rejectHelp') }}</p>
      <a-form ref="rejectFormRef" :model="rejectForm" layout="vertical">
        <a-form-item :label="t('field.taskNo')"><a-input :value="selectedTask" disabled /></a-form-item>
        <a-form-item name="reason" :label="t('field.rejectReason')" required :rules="[{ required: true, message: t('task.reasonPlaceholder') }]">
          <a-textarea v-model:value="rejectForm.reason" :rows="4" :placeholder="t('task.reasonPlaceholder')" />
        </a-form-item>
      </a-form>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FormInstance, TableColumnsType } from 'ant-design-vue'
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { tasks } from '@/services/mockData'

const { t } = useI18n()
const rejectOpen = ref(false)
const selectedTask = ref('')
const rejectFormRef = ref<FormInstance>()
const rejectForm = reactive({ reason: '' })
const statusOptions = computed(() => [{ value: 'pendingApproval', label: t('status.pendingApproval') }])
const c = (key: string, field: string, width = 150) => ({ title: t(key), dataIndex: field, key: field, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  { title: t('field.status'), key: 'status', width: 180 }, { title: t('field.result'), key: 'result', width: 130 }, c('field.generatedTime', 'generated', 170),
  c('field.taskNo', 'no', 150), c('field.planName', 'plan', 210), c('field.standardName', 'standard', 250), c('field.factory', 'factory'), c('field.line', 'line'),
  c('field.inspector', 'inspector', 150), c('field.submittedTime', 'submitted', 170), { title: t('common.actions'), key: 'actions', fixed: 'right', width: 160 },
])
function openReject(taskNo: string) { selectedTask.value = taskNo; rejectForm.reason = ''; rejectOpen.value = true }
async function confirmReject() {
  try {
    await rejectFormRef.value?.validate()
    rejectOpen.value = false
  } catch {
    // Ant Design rejects the validation promise when required fields are empty.
  }
}
</script>
