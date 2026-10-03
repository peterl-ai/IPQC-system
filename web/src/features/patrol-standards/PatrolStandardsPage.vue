<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.linePatrol')" :title="t('page.standards.title')" :subtitle="t('page.standards.subtitle')" />

    <div class="data-panel">
      <div class="filter-bar">
        <a-form layout="inline" @finish="applyFilters">
          <a-form-item :label="t('filter.standardName')">
            <a-input v-model:value="pendingFilters.name" allow-clear :placeholder="t('filter.placeholder', { field: t('filter.standardName') })" />
          </a-form-item>
          <a-form-item :label="t('filter.factory')">
            <a-select v-model:value="pendingFilters.factory" allow-clear :placeholder="t('filter.select', { field: t('filter.factory') })" :options="factoryOptions" />
          </a-form-item>
          <a-form-item :label="t('filter.line')">
            <a-select v-model:value="pendingFilters.line" allow-clear :placeholder="t('filter.select', { field: t('filter.line') })" :options="lineOptions" />
          </a-form-item>
        </a-form>
        <div class="filter-actions">
          <a-button type="primary" @click="applyFilters"><SearchOutlined />{{ t('common.search') }}</a-button>
          <a-button @click="resetFilters"><ReloadOutlined />{{ t('common.reset') }}</a-button>
        </div>
      </div>

      <div class="table-toolbar standards-toolbar">
        <div>
          <a-button type="primary" @click="openNew"><PlusOutlined />{{ t('action.new') }}</a-button>
          <a-button @click="openSelectedForEdit"><SaveOutlined />{{ t('action.save') }}</a-button>
          <a-button @click="openSelectedCopy"><CopyOutlined />{{ t('action.copy') }}</a-button>
        </div>
        <div>
          <span v-if="selectedImportFile" class="selected-file">{{ t('editor.selectedFile', { file: selectedImportFile }) }}</span>
          <a-button @click="showExportMessage"><ExportOutlined />{{ t('action.export') }}</a-button>
          <a-upload accept=".xlsx" :before-upload="selectImportFile" :show-upload-list="false">
            <a-button><ImportOutlined />{{ t('action.import') }}</a-button>
          </a-upload>
          <a-button @click="showTemplateMessage"><DownloadOutlined />{{ t('action.template') }}</a-button>
        </div>
      </div>

      <a-table
        row-key="id"
        size="small"
        :columns="columns"
        :data-source="filteredStandards"
        :row-selection="rowSelection"
        :scroll="{ x: 1900 }"
        :pagination="pagination"
      >
        <template #bodyCell="{ column, record, index }">
          <template v-if="column.key === 'no'">{{ index + 1 }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openEdit(record)">{{ t('common.edit') }}</a-button>
              <a-popconfirm
                :title="t('editor.deleteTitle')"
                :description="t('editor.deleteConfirm')"
                :ok-text="t('common.confirm')"
                :cancel-text="t('common.cancel')"
                @confirm="removeStandard(record.id)"
              >
                <a-button type="link" danger size="small">{{ t('common.delete') }}</a-button>
              </a-popconfirm>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>
    </div>

    <a-drawer v-model:open="editorOpen" :title="editorTitle" width="min(1180px, 94vw)" class="editor-drawer" :mask-closable="false">
      <a-alert type="info" show-icon :message="t('editor.nonPersistent')" />

      <h3 class="section-title">{{ t('editor.header') }}</h3>
      <a-form ref="editorFormRef" :model="editorForm" :rules="editorRules" layout="vertical" class="editor-form">
        <a-form-item :label="t('field.factoryCode')"><a-input v-model:value="editorForm.factoryCode" /></a-form-item>
        <a-form-item :label="t('field.factoryName')"><a-input v-model:value="editorForm.factoryName" /></a-form-item>
        <a-form-item :label="t('field.workshopCode')"><a-input v-model:value="editorForm.workshopCode" /></a-form-item>
        <a-form-item :label="t('field.lineCode')"><a-input v-model:value="editorForm.lineCode" /></a-form-item>
        <a-form-item name="lineName" :label="t('field.lineName')"><a-input v-model:value="editorForm.lineName" /></a-form-item>
        <a-form-item name="standardName" :label="t('field.standardName')"><a-input v-model:value="editorForm.standardName" /></a-form-item>
        <a-form-item :label="t('field.materialCode')"><a-input v-model:value="editorForm.materialCode" /></a-form-item>
      </a-form>

      <div class="section-title-row">
        <h3 class="section-title">{{ t('editor.detail') }}</h3>
        <a-button @click="openNewItem"><PlusOutlined />{{ t('editor.addItem') }}</a-button>
      </div>
      <a-table row-key="id" size="small" :columns="detailColumns" :data-source="editorItems" :scroll="{ x: 2250 }" :pagination="false" bordered>
        <template #bodyCell="{ column, index }">
          <template v-if="column.key === 'no'">{{ index + 1 }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-space size="small">
              <a-button type="link" size="small" @click="openEditItem(index)">{{ t('common.edit') }}</a-button>
              <a-button type="link" size="small" :disabled="index === 0" @click="moveItem(index, -1)">{{ t('action.moveUp') }}</a-button>
              <a-button type="link" size="small" :disabled="index === editorItems.length - 1" @click="moveItem(index, 1)">{{ t('action.moveDown') }}</a-button>
              <a-popconfirm :title="t('common.delete')" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" @confirm="deleteItem(index)">
                <a-button type="link" danger size="small">{{ t('common.delete') }}</a-button>
              </a-popconfirm>
            </a-space>
          </template>
        </template>
        <template #emptyText><a-empty :description="t('common.noData')" /></template>
      </a-table>

      <template #footer>
        <a-space>
          <a-button type="primary" @click="saveEditor">{{ t('action.save') }}</a-button>
          <a-button @click="editorOpen = false">{{ t('common.cancel') }}</a-button>
        </a-space>
      </template>
    </a-drawer>

    <a-modal v-model:open="itemEditorOpen" :title="itemEditorIndex === null ? t('editor.addItem') : t('editor.editItem')" width="920px" :ok-text="t('action.save')" :cancel-text="t('common.cancel')" @ok="saveItem">
      <a-form :model="itemForm" layout="vertical" class="item-editor-form">
        <a-form-item :label="t('field.processCode')"><a-input v-model:value="itemForm.processCode" /></a-form-item>
        <a-form-item :label="t('field.processName')"><a-input v-model:value="itemForm.processName" /></a-form-item>
        <a-form-item :label="t('field.itemCategory')"><a-input v-model:value="itemForm.itemCategory" /></a-form-item>
        <a-form-item :label="t('field.inspectionItem')"><a-input v-model:value="itemForm.inspectionItem" /></a-form-item>
        <a-form-item class="item-editor-wide" :label="t('field.inspectionContent')"><a-textarea v-model:value="itemForm.inspectionContent" :rows="2" /></a-form-item>
        <a-form-item :label="t('field.upperOperator')"><a-input v-model:value="itemForm.upperOperator" /></a-form-item>
        <a-form-item :label="t('field.upperValue')"><a-input v-model:value="itemForm.upperValue" /></a-form-item>
        <a-form-item :label="t('field.lowerOperator')"><a-input v-model:value="itemForm.lowerOperator" /></a-form-item>
        <a-form-item :label="t('field.lowerValue')"><a-input v-model:value="itemForm.lowerValue" /></a-form-item>
        <a-form-item :label="t('field.inspectionType')"><a-input v-model:value="itemForm.inspectionType" /></a-form-item>
        <a-form-item :label="t('field.samplingPlan')"><a-input v-model:value="itemForm.samplingPlan" /></a-form-item>
        <a-form-item :label="t('field.sampleCount')"><a-input v-model:value="itemForm.sampleCount" /></a-form-item>
        <a-form-item :label="t('field.photoRequirement')"><a-input v-model:value="itemForm.photoRequirement" /></a-form-item>
        <a-form-item :label="t('field.defectLevel')"><a-input v-model:value="itemForm.defectLevel" /></a-form-item>
      </a-form>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, nextTick, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { message, type FormInstance, type TableColumnsType, type UploadProps } from 'ant-design-vue'
import { CopyOutlined, DownloadOutlined, ExportOutlined, ImportOutlined, PlusOutlined, ReloadOutlined, SaveOutlined, SearchOutlined } from '@ant-design/icons-vue'
import PageHeader from '@/components/PageHeader.vue'
import { standards as seedStandards } from '@/services/mockData'
import { mockSession } from '@/services/mockSession'
import {
  copyMockPatrolStandard,
  createMockPatrolStandard,
  deleteMockPatrolStandard,
  emptyPatrolStandardDraft,
  emptyPatrolStandardItem,
  isInspectionItemEmpty,
  toPatrolStandardDraft,
  updateMockPatrolStandard,
  type PatrolStandard,
  type PatrolStandardDraft,
  type PatrolStandardItem,
} from './model'
import { buildPatrolStandardRequiredRules, isXlsxFileName } from './validation'

type EditorMode = 'new' | 'edit' | 'copy'
type EditorForm = Omit<PatrolStandardDraft, 'name' | 'inspectionItems'> & { standardName: string }

const { t } = useI18n()
const standards = ref<PatrolStandard[]>(seedStandards.map((standard) => ({ ...standard, inspectionItems: standard.inspectionItems.map((item) => ({ ...item })) })))
const selectedRowKeys = ref<string[]>([])
const selectedImportFile = ref('')
const pendingFilters = reactive({ name: '', factory: undefined as string | undefined, line: undefined as string | undefined })
const activeFilters = reactive({ name: '', factory: undefined as string | undefined, line: undefined as string | undefined })

const editorOpen = ref(false)
const editorMode = ref<EditorMode>('new')
const editingId = ref<string | null>(null)
const editorFormRef = ref<FormInstance>()
const editorForm = reactive<EditorForm>({
  factoryCode: '',
  factoryName: '',
  workshopCode: '',
  lineCode: '',
  lineName: '',
  standardName: '',
  materialCode: '',
})
const editorItems = ref<PatrolStandardItem[]>([])

const itemEditorOpen = ref(false)
const itemEditorIndex = ref<number | null>(null)
const itemForm = reactive<PatrolStandardItem>(emptyPatrolStandardItem(''))
let mockSequence = 100

const editorRules = computed(() => buildPatrolStandardRequiredRules({
  lineName: t('editor.validation.lineName'),
  standardName: t('editor.validation.standardName'),
}))
const editorTitle = computed(() => t(editorMode.value === 'new' ? 'editor.createTitle' : editorMode.value === 'copy' ? 'editor.copyTitle' : 'editor.editTitle'))
const factoryOptions = computed(() => uniqueOptions(standards.value.map((standard) => standard.factoryCode)))
const lineOptions = computed(() => uniqueOptions(standards.value.map((standard) => standard.lineCode)))
const filteredStandards = computed(() => standards.value.filter((standard) => {
  const nameMatches = standard.name.toLocaleLowerCase().includes(activeFilters.name.trim().toLocaleLowerCase())
  const factoryMatches = !activeFilters.factory || standard.factoryCode === activeFilters.factory
  const lineMatches = !activeFilters.line || standard.lineCode === activeFilters.line
  return nameMatches && factoryMatches && lineMatches
}))
const rowSelection = computed(() => ({
  type: 'radio' as const,
  selectedRowKeys: selectedRowKeys.value,
  onChange: (keys: (string | number)[]) => { selectedRowKeys.value = keys.map(String) },
}))
const pagination = computed(() => ({ pageSize: 10, showSizeChanger: true, showTotal: (total: number) => `${total}` }))
const column = (title: string, dataIndex: string, width = 140) => ({ title: t(title), dataIndex, key: dataIndex, width, ellipsis: true })
const columns = computed<TableColumnsType>(() => [
  { title: t('field.no'), key: 'no', width: 70 },
  column('field.standardName', 'name', 230), column('field.factoryCode', 'factoryCode'), column('field.factoryName', 'factoryName', 180),
  column('field.workshopCode', 'workshopCode'), column('field.lineCode', 'lineCode'), column('field.lineName', 'lineName', 170),
  column('field.materialCode', 'materialCode'), column('field.createdBy', 'createdBy'), column('field.createdTime', 'createdTime', 170),
  column('field.updatedBy', 'updatedBy'), column('field.updatedTime', 'updatedTime', 170),
  { title: t('common.actions'), key: 'actions', fixed: 'right', width: 140 },
])
const detailColumns = computed<TableColumnsType>(() => [
  { title: t('field.no'), key: 'no', width: 60, fixed: 'left' },
  column('field.processCode', 'processCode'), column('field.processName', 'processName'), column('field.itemCategory', 'itemCategory', 180),
  column('field.inspectionItem', 'inspectionItem', 180), column('field.inspectionContent', 'inspectionContent', 220),
  column('field.upperOperator', 'upperOperator'), column('field.upperValue', 'upperValue'), column('field.lowerOperator', 'lowerOperator'),
  column('field.lowerValue', 'lowerValue'), column('field.inspectionType', 'inspectionType'), column('field.samplingPlan', 'samplingPlan', 170),
  column('field.sampleCount', 'sampleCount'), column('field.photoRequirement', 'photoRequirement', 170), column('field.defectLevel', 'defectLevel'),
  { title: t('common.actions'), key: 'actions', fixed: 'right', width: 310 },
])

function uniqueOptions(values: string[]) {
  return [...new Set(values.filter(Boolean))].map((value) => ({ value, label: value }))
}

function applyFilters() {
  Object.assign(activeFilters, pendingFilters)
  selectedRowKeys.value = []
}

function resetFilters() {
  Object.assign(pendingFilters, { name: '', factory: undefined, line: undefined })
  Object.assign(activeFilters, pendingFilters)
  selectedRowKeys.value = []
}

function selectedStandard(): PatrolStandard | undefined {
  return standards.value.find((standard) => standard.id === selectedRowKeys.value[0])
}

function openSelectedForEdit() {
  const standard = selectedStandard()
  if (!standard) return void message.warning(t('editor.selectRecord'))
  openEdit(standard)
}

function openSelectedCopy() {
  const standard = selectedStandard()
  if (!standard) return void message.warning(t('editor.selectRecord'))
  openCopy(standard)
}

async function openNew() {
  await openEditor('new', emptyPatrolStandardDraft())
}

async function openEdit(standard: PatrolStandard) {
  selectedRowKeys.value = [standard.id]
  editingId.value = standard.id
  await openEditor('edit', toPatrolStandardDraft(standard))
}

async function openCopy(standard: PatrolStandard) {
  const draft = copyMockPatrolStandard(standard, () => nextId('item'))
  draft.name = t('editor.copiedName', { name: draft.name })
  editingId.value = null
  await openEditor('copy', draft)
}

async function openEditor(mode: EditorMode, draft: PatrolStandardDraft) {
  editorMode.value = mode
  if (mode === 'new') editingId.value = null
  Object.assign(editorForm, {
    factoryCode: draft.factoryCode,
    factoryName: draft.factoryName,
    workshopCode: draft.workshopCode,
    lineCode: draft.lineCode,
    lineName: draft.lineName,
    standardName: draft.name,
    materialCode: draft.materialCode,
  })
  editorItems.value = draft.inspectionItems.map((item) => ({ ...item }))
  editorOpen.value = true
  await nextTick()
  editorFormRef.value?.clearValidate()
}

async function saveEditor() {
  try {
    await editorFormRef.value?.validate()
  } catch {
    return
  }
  const draft: PatrolStandardDraft = {
    name: editorForm.standardName.trim(),
    factoryCode: editorForm.factoryCode.trim(),
    factoryName: editorForm.factoryName.trim(),
    workshopCode: editorForm.workshopCode.trim(),
    lineCode: editorForm.lineCode.trim(),
    lineName: editorForm.lineName.trim(),
    materialCode: editorForm.materialCode.trim(),
    inspectionItems: editorItems.value.map((item) => ({ ...item })),
  }
  const audit = { user: mockSession.userName.value, time: mockTime() }
  if (editorMode.value === 'edit' && editingId.value) {
    standards.value = updateMockPatrolStandard(standards.value, editingId.value, draft, audit)
  } else {
    standards.value = createMockPatrolStandard(standards.value, draft, { ...audit, id: nextId('std') })
  }
  message.success(t('editor.mockSaved'))
  editorOpen.value = false
}

function removeStandard(id: string) {
  standards.value = deleteMockPatrolStandard(standards.value, id)
  if (selectedRowKeys.value.includes(id)) selectedRowKeys.value = []
  message.success(t('editor.deleted'))
}

function openNewItem() {
  itemEditorIndex.value = null
  Object.assign(itemForm, emptyPatrolStandardItem(nextId('item')))
  itemEditorOpen.value = true
}

function openEditItem(index: number) {
  const item = editorItems.value[index]
  if (!item) return
  itemEditorIndex.value = index
  Object.assign(itemForm, item)
  itemEditorOpen.value = true
}

function saveItem() {
  if (isInspectionItemEmpty(itemForm)) {
    message.error(t('editor.emptyItem'))
    return
  }
  const saved = { ...itemForm }
  if (itemEditorIndex.value === null) editorItems.value.push(saved)
  else editorItems.value.splice(itemEditorIndex.value, 1, saved)
  itemEditorOpen.value = false
}

function deleteItem(index: number) {
  editorItems.value.splice(index, 1)
}

function moveItem(index: number, offset: -1 | 1) {
  const target = index + offset
  const item = editorItems.value[index]
  if (!item || target < 0 || target >= editorItems.value.length) return
  editorItems.value.splice(index, 1)
  editorItems.value.splice(target, 0, item)
}

const selectImportFile: UploadProps['beforeUpload'] = (file) => {
  if (!isXlsxFileName(file.name)) {
    message.error(t('editor.invalidImportFile'))
    return false
  }
  selectedImportFile.value = file.name
  message.info(t('editor.importDeferred', { file: file.name }))
  return false
}

function showExportMessage() {
  message.info(t('editor.exportDeferred'))
}

function showTemplateMessage() {
  message.info(t('editor.templateDeferred'))
}

function nextId(prefix: string): string {
  mockSequence += 1
  return `${prefix}-mock-${mockSequence}`
}

function mockTime(): string {
  const date = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}
</script>
