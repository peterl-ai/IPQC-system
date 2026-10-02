<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.system')" :title="t('page.users.title')" :subtitle="t('page.users.subtitle')" />
    <div class="data-panel">
      <div class="filter-bar">
        <a-form layout="inline"><a-form-item :label="t('filter.keyword')"><a-input allow-clear :placeholder="t('filter.placeholder', { field: t('filter.keyword') })" /></a-form-item><a-form-item :label="t('field.role')"><a-select allow-clear :placeholder="t('filter.select', { field: t('field.role') })" :options="roleOptions" /></a-form-item></a-form>
        <div class="filter-actions"><a-button type="primary"><SearchOutlined />{{ t('common.search') }}</a-button><a-button><ReloadOutlined />{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar"><div><a-button type="primary"><UserAddOutlined />{{ t('action.addUser') }}</a-button></div></div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="users" :pagination="{ pageSize: 10, showSizeChanger: true }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'role'"><a-tag color="blue">{{ t(`role.${record.role}`) }}</a-tag></template>
          <template v-else-if="column.key === 'active'"><StatusTag :tone="record.active ? 'success' : 'neutral'" :label="record.active ? t('common.active') : t('common.inactive')" /></template>
          <template v-else-if="column.key === 'actions'"><a-button type="link" size="small">{{ t('common.edit') }}</a-button></template>
        </template>
      </a-table>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnsType } from 'ant-design-vue'
import { ReloadOutlined, SearchOutlined, UserAddOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { users } from '@/services/mockData'

const { t } = useI18n()
const roleOptions = computed(() => ['admin', 'pqe', 'ipqa'].map((value) => ({ value, label: t(`role.${value}`) })))
const columns = computed<TableColumnsType>(() => [
  { title: t('field.username'), dataIndex: 'username', key: 'username', width: 200 }, { title: t('field.displayName'), dataIndex: 'name', key: 'name', width: 200 },
  { title: t('field.role'), key: 'role', width: 130 }, { title: t('field.active'), key: 'active', width: 120 }, { title: t('field.createdTime'), dataIndex: 'created', key: 'created', width: 180 },
  { title: t('common.actions'), key: 'actions', width: 100 },
])
</script>
