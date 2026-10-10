<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.system')" :title="t('page.users.title')" :subtitle="t('page.users.subtitle')" />
    <div class="data-panel">
      <a-alert v-if="error" type="error" show-icon :message="error" style="margin-bottom: 12px" />
      <div class="filter-bar">
        <a-form layout="inline" @finish="search">
          <a-form-item :label="t('field.username')"><a-input v-model:value="filters.username" allow-clear /></a-form-item>
          <a-form-item :label="t('field.displayName')"><a-input v-model:value="filters.displayName" allow-clear /></a-form-item>
          <a-form-item :label="t('field.role')"><a-select v-model:value="filters.role" allow-clear :options="roleOptions" style="width: 120px" /></a-form-item>
          <a-form-item :label="t('field.active')"><a-select v-model:value="filters.active" allow-clear :options="activeOptions" style="width: 120px" /></a-form-item>
        </a-form>
        <div class="filter-actions"><a-button type="primary" @click="search">{{ t('common.search') }}</a-button><a-button @click="resetFilters">{{ t('common.reset') }}</a-button></div>
      </div>
      <div class="table-toolbar"><a-button type="primary" @click="openCreate">{{ t('action.addUser') }}</a-button></div>
      <a-table row-key="id" size="small" :columns="columns" :data-source="rows" :loading="loading" :scroll="{ x: 1500 }" :pagination="pagination">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'role'">{{ t(`role.${record.role.toLowerCase()}`) }}</template>
          <template v-else-if="column.key === 'isActive'"><StatusTag :tone="record.isActive ? 'success' : 'neutral'" :label="record.isActive ? t('common.active') : t('common.inactive')" /></template>
          <template v-else-if="column.key === 'mustChangePassword'">{{ record.mustChangePassword ? t('user.yes') : t('user.no') }}</template>
          <template v-else-if="column.key === 'createdAtUtc' || column.key === 'updatedAtUtc'">{{ plantDate(record[column.key]) }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openEdit(record)">{{ t('common.edit') }}</a-button>
              <a-button type="link" size="small" @click="openPassword(record)">{{ t('user.resetPassword') }}</a-button>
              <a-popconfirm :title="record.isActive ? t('user.deactivateConfirm') : t('user.activateConfirm')" @confirm="toggle(record)">
                <a-button type="link" size="small">{{ record.isActive ? t('user.deactivate') : t('user.activate') }}</a-button>
              </a-popconfirm>
              <a-button v-if="record.lockoutEndUtc" type="link" size="small" @click="unlock(record)">{{ t('user.unlock') }}</a-button>
            </a-space>
          </template>
        </template>
      </a-table>
    </div>
    <a-modal v-model:open="editorOpen" :title="editingId ? t('user.editTitle') : t('user.newTitle')" :confirm-loading="busy" @ok="save">
      <a-alert v-if="modalError" type="error" show-icon :message="modalError" style="margin-bottom: 12px" />
      <a-form layout="vertical">
        <a-form-item :label="t('field.username')" required><a-input v-model:value="draft.username" :disabled="!!editingId" autocomplete="off" /></a-form-item>
        <a-form-item :label="t('field.displayName')" required><a-input v-model:value="draft.displayName" /></a-form-item>
        <a-form-item :label="t('field.role')" required><a-select v-model:value="draft.role" :options="roleOptions" /></a-form-item>
        <template v-if="!editingId">
          <p>{{ t('auth.passwordPolicy') }}</p>
          <a-form-item :label="t('user.initialPassword')" required><a-input-password v-model:value="draft.initialPassword" autocomplete="new-password" /></a-form-item>
          <a-form-item :label="t('auth.confirmPassword')" required><a-input-password v-model:value="confirm" autocomplete="new-password" /></a-form-item>
          <a-form-item :label="t('field.active')"><a-switch v-model:checked="draft.isActive" /></a-form-item>
        </template>
      </a-form>
    </a-modal>
    <a-modal v-model:open="passwordOpen" :title="t('user.resetPassword')" :confirm-loading="busy" @ok="resetPassword">
      <a-alert v-if="modalError" type="error" show-icon :message="modalError" style="margin-bottom: 12px" />
      <p>{{ t('auth.passwordPolicy') }}</p>
      <a-form layout="vertical">
        <a-form-item :label="t('user.initialPassword')" required><a-input-password v-model:value="temporaryPassword" autocomplete="new-password" /></a-form-item>
        <a-form-item :label="t('auth.confirmPassword')" required><a-input-password v-model:value="confirm" autocomplete="new-password" /></a-form-item>
      </a-form>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { message, type TableColumnsType } from 'ant-design-vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { usersApi, type UserCreate, type UserFilters, type UserRow } from './api'

const { t } = useI18n()
const rows = ref<UserRow[]>([])
const filters = reactive<UserFilters>({ username: '', displayName: '', role: undefined, active: null, page: 1, pageSize: 10 })
const total = ref(0)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const modalError = ref('')
const editorOpen = ref(false)
const passwordOpen = ref(false)
const editingId = ref<string | null>(null)
const temporaryPassword = ref('')
const confirm = ref('')
const targetId = ref('')
const emptyDraft = (): UserCreate => ({ username: '', displayName: '', role: 'IPQA', initialPassword: '', isActive: true })
const draft = reactive<UserCreate>(emptyDraft())
const roleOptions = computed(() => (['Admin', 'PQE', 'IPQA'] as const).map(value => ({ value, label: t(`role.${value.toLowerCase()}`) })))
const activeOptions = computed(() => [{ value: true, label: t('common.active') }, { value: false, label: t('common.inactive') }])
const columns = computed<TableColumnsType>(() => [
  { title: t('field.username'), dataIndex: 'username', width: 170 },
  { title: t('field.displayName'), dataIndex: 'displayName', width: 170 },
  { title: t('field.role'), key: 'role', width: 90 },
  { title: t('field.active'), key: 'isActive', width: 90 },
  { title: t('user.mustChangePassword'), key: 'mustChangePassword', width: 120 },
  { title: t('field.createdTime'), key: 'createdAtUtc', width: 160 },
  { title: t('field.createdBy'), dataIndex: 'createdBy', width: 120 },
  { title: t('field.updatedTime'), key: 'updatedAtUtc', width: 160 },
  { title: t('field.updatedBy'), dataIndex: 'updatedBy', width: 120 },
  { title: t('common.actions'), key: 'actions', width: 340, fixed: 'right' },
])
const pagination = computed(() => ({ current: filters.page, pageSize: filters.pageSize, total: total.value,
  showSizeChanger: true, onChange: (page: number, pageSize: number) => { filters.page = page; filters.pageSize = pageSize; void load() } }))
const plantDate = (value: string) => new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
async function load() {
  loading.value = true; error.value = ''
  try { const data = await usersApi.list(filters); rows.value = data.items; total.value = data.total }
  catch (cause) { error.value = cause instanceof Error ? cause.message : t('user.loadFailed') }
  finally { loading.value = false }
}
function search() { filters.page = 1; void load() }
function resetFilters() { Object.assign(filters, { username: '', displayName: '', role: undefined, active: null, page: 1 }); void load() }
function openCreate() { editingId.value = null; Object.assign(draft, emptyDraft()); confirm.value = ''; modalError.value = ''; editorOpen.value = true }
function openEdit(row: UserRow) { editingId.value = row.id; Object.assign(draft, { username: row.username, displayName: row.displayName, role: row.role, initialPassword: '', isActive: row.isActive }); modalError.value = ''; editorOpen.value = true }
async function save() {
  if (!draft.displayName.trim() || !draft.username.trim() || (!editingId.value && draft.initialPassword !== confirm.value))
  { modalError.value = t('user.validation'); return }
  busy.value = true; modalError.value = ''
  try {
    if (editingId.value) await usersApi.update(editingId.value, draft.displayName, draft.role)
    else await usersApi.create(draft)
    editorOpen.value = false; draft.initialPassword = ''; confirm.value = ''; await load()
    message.success(t('user.saved'))
  } catch (cause) { modalError.value = cause instanceof Error ? cause.message : t('user.saveFailed') }
  finally { busy.value = false }
}
function openPassword(row: UserRow) { targetId.value = row.id; temporaryPassword.value = ''; confirm.value = ''; modalError.value = ''; passwordOpen.value = true }
async function resetPassword() {
  if (!temporaryPassword.value || temporaryPassword.value !== confirm.value) { modalError.value = t('user.validation'); return }
  busy.value = true; modalError.value = ''
  try { await usersApi.resetPassword(targetId.value, temporaryPassword.value); passwordOpen.value = false; await load(); message.success(t('user.saved')) }
  catch (cause) { modalError.value = cause instanceof Error ? cause.message : t('user.saveFailed') }
  finally { temporaryPassword.value = ''; confirm.value = ''; busy.value = false }
}
async function toggle(row: UserRow) {
  try { await usersApi.setActive(row.id, !row.isActive); await load() }
  catch (cause) { message.error(cause instanceof Error ? cause.message : t('user.saveFailed')) }
}
async function unlock(row: UserRow) {
  try { await usersApi.unlock(row.id); await load() }
  catch (cause) { message.error(cause instanceof Error ? cause.message : t('user.saveFailed')) }
}
onMounted(load)
</script>
