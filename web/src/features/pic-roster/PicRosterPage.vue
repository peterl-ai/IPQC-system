<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.system')" :title="t('picRoster.title')" :subtitle="t('picRoster.subtitle')" />

    <a-alert v-if="!actor" type="warning" :message="t('picRoster.error403')" />
    <template v-else>
      <div class="data-panel roster-panel">
        <div class="table-toolbar roster-toolbar">
          <label for="pic-status">{{ t('picRoster.status') }}</label>
          <select id="pic-status" :value="filter" :disabled="loading || busy" @change="changeFilter">
            <option value="all">{{ t('picRoster.all') }}</option>
            <option value="active">{{ t('common.active') }}</option>
            <option value="inactive">{{ t('common.inactive') }}</option>
          </select>
          <a-button :disabled="loading || busy" @click="refresh">{{ t('picRoster.refresh') }}</a-button>
          <a-button v-if="writable" type="primary" :disabled="!newWrite" @click="openEditor('Created')">
            {{ t('picRoster.create') }}
          </a-button>
        </div>
        <a-alert v-if="listError" type="error" show-icon role="alert" :message="t('picRoster.' + listError)" />
        <a-alert v-if="mutationError" type="error" show-icon role="alert" :message="t('picRoster.' + mutationError)" />

        <div v-if="pending" class="roster-detail" role="status">
          <strong>{{ t(accepted ? 'picRoster.refreshPending' : 'picRoster.unknown') }}</strong>
          <p>{{ t('picRoster.requestId') }} <code>{{ pending.requestId }}</code></p>
          <p>{{ t('picRoster.' + pending.action) }} · {{ pending.values.name }} · {{ pending.values.department }}</p>
          <a-space wrap>
            <a-button :disabled="busy" @click="recover('receipt')">{{ t('picRoster.lookup') }}</a-button>
            <a-button v-if="writable && !accepted" :disabled="busy || storageFailed" @click="recover('retry')">
              {{ t('picRoster.retry') }}
            </a-button>
          </a-space>
        </div>

        <div v-if="accepted || conflict" class="roster-detail" role="status">
          <template v-if="accepted">
            <strong>{{ t('picRoster.originalReceipt') }}</strong>
            <p>
              {{ t('picRoster.' + accepted.action.toLowerCase()) }} · {{ accepted.name }} · {{ accepted.department }} ·
              {{ status(accepted.isActive) }} · {{ t('picRoster.revision') }} {{ accepted.revision }}
            </p>
            <p>{{ t('picRoster.picId') }} <code>{{ accepted.picId }}</code></p>
            <p>{{ t('picRoster.requestId') }} <code>{{ accepted.requestId }}</code></p>
            <p>{{ accepted.actorUsername }} · {{ picTime(accepted.occurredAtUtc, locale) }}</p>
          </template>
          <strong v-if="conflict">{{ t('picRoster.reviewConflict') }}</strong>
          <p v-if="current">
            {{ t('picRoster.current') }} · {{ current.name }} · {{ current.department }} ·
            {{ status(current.isActive) }} · {{ t('picRoster.revision') }} {{ current.revision }}
          </p>
          <a-button
            v-if="writable && conflict && current && !pending && !storageFailed" :disabled="busy"
            @click="reviewCurrent"
          >
            {{ t('picRoster.useCurrent') }}
          </a-button>
        </div>

        <a-modal
          :open="!!editor && writable && editorVisible" :title="editor ? t('picRoster.' + editor.action) : ''"
          :footer="null" :closable="!busy" :mask-closable="!busy" :keyboard="!busy" @cancel="closeEditor"
        >
          <form v-if="editor && writable" class="roster-form" @submit.prevent="save">
            <a-alert v-if="mutationError" type="error" :message="t('picRoster.' + mutationError)" />
            <p v-if="editor.target">
              {{ t('picRoster.picId') }} <code>{{ editor.target.picId }}</code> ·
              {{ t('picRoster.revision') }} {{ editor.target.revision }}
            </p>
            <template v-if="editor.action === 'Created' || editor.action === 'Updated'">
              <div class="roster-fields">
                <label>
                  {{ t('picRoster.name') }}
                  <a-input v-model:value="editor.values.name" :disabled="busy || !!pending" :aria-invalid="invalidName" />
                  <span v-if="invalidName" role="alert">{{ t('picRoster.invalidText') }}</span>
                </label>
                <label>
                  {{ t('picRoster.department') }}
                  <a-input v-model:value="editor.values.department" :disabled="busy || !!pending" :aria-invalid="invalidDepartment" />
                  <span v-if="invalidDepartment" role="alert">{{ t('picRoster.invalidText') }}</span>
                </label>
                <label v-if="editor.action === 'Created'">
                  <input v-model="editor.values.isActive" type="checkbox" :disabled="busy || !!pending">
                  {{ t('common.active') }}
                </label>
              </div>
            </template>
            <p v-else>{{ t(editor.action === 'Activated' ? 'picRoster.confirmActivate' : 'picRoster.confirmDeactivate', { name: editor.values.name }) }}</p>
            <a-space>
              <a-button type="primary" html-type="submit" :disabled="!newWrite">
                {{ t(editor.action === 'Created' || editor.action === 'Updated' ? 'action.save' : 'picRoster.confirm') }}
              </a-button>
              <a-button :disabled="busy || !!pending || conflict" @click="closeEditor">{{ t('common.cancel') }}</a-button>
            </a-space>
          </form>
        </a-modal>

        <a-spin v-if="loading" :tip="t('picRoster.loading')" />
        <a-table
          v-if="list" size="small" row-key="picId" :columns="columns" :data-source="list.items"
          :pagination="false" :scroll="{ x: 860 }" :locale="{ emptyText: t('common.noData') }"
        >
          <template #bodyCell="{ column, record }">
            <code v-if="column.key === 'picId'">{{ record.picId }}</code>
            <StatusTag v-else-if="column.key === 'isActive'" :tone="record.isActive ? 'success' : 'neutral'" :label="status(record.isActive)" />
            <a-space v-else-if="column.key === 'actions'" wrap>
              <template v-if="writable">
                <a-button type="link" :disabled="!newWrite" @click="openEditor('Updated', record)">{{ t('common.edit') }}</a-button>
                <a-button type="link" :disabled="!newWrite" @click="openEditor(record.isActive ? 'Deactivated' : 'Activated', record)">
                  {{ t(record.isActive ? 'picRoster.deactivate' : 'picRoster.activate') }}
                </a-button>
              </template>
              <a-button type="link" @click="openHistory(record)">{{ t('picRoster.history') }}</a-button>
            </a-space>
          </template>
        </a-table>
        <a-space v-if="list">
          <a-button :disabled="loading || busy || list.page === 1" @click="loadList(list.page - 1)">{{ t('picRoster.previous') }}</a-button>
          <span>{{ t('picRoster.page', { page: list.page }) }}</span>
          <a-button :disabled="loading || busy || !list.hasMore" @click="loadList(list.page + 1)">{{ t('picRoster.next') }}</a-button>
        </a-space>
      </div>

      <div v-if="historyId" class="data-panel roster-panel">
        <div class="table-toolbar roster-toolbar">
          <h3>{{ t('picRoster.history') }}</h3>
          <code>{{ historyId }}</code>
          <a-button @click="closeHistory">{{ t('picRoster.close') }}</a-button>
        </div>
        <a-spin v-if="historyLoading" :tip="t('picRoster.loading')" />
        <a-alert v-if="historyError" type="error" show-icon role="alert" :message="t('picRoster.' + historyError)" />
        <a-table
          v-if="history" size="small" row-key="requestId" :columns="historyColumns" :data-source="history.items"
          :pagination="false" :scroll="{ x: 1100 }" :locale="{ emptyText: t('common.noData') }"
        >
          <template #bodyCell="{ column, record }">
            <code v-if="column.key === 'requestId'">{{ record.requestId }}</code>
            <span v-else-if="column.key === 'actorUsername'">{{ record.actorUsername }} · <code>{{ record.actorUserId }}</code></span>
            <template v-else-if="column.key === 'isActive'">{{ status(record.isActive) }}</template>
            <template v-else-if="column.key === 'action'">{{ t('picRoster.' + record.action.toLowerCase()) }}</template>
            <template v-else-if="column.key === 'occurredAtUtc'">{{ picTime(record.occurredAtUtc, locale) }}</template>
          </template>
        </a-table>
        <a-space v-if="history">
          <a-button :disabled="historyLoading || history.page === 1" @click="loadHistory(history.page - 1)">{{ t('picRoster.previous') }}</a-button>
          <span>{{ t('picRoster.page', { page: history.page }) }}</span>
          <a-button :disabled="historyLoading || !history.hasMore" @click="loadHistory(history.page + 1)">{{ t('picRoster.next') }}</a-button>
        </a-space>
      </div>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { authSession } from '@/services/authSession'
import { canReadPicRoster, canWritePicRoster } from '@/services/permissions'
import { picRosterApi } from './api'
import { normalizePicText, picTime, type Pic, type PicAction, type PicPage, type PicReceipt, type PicValues } from './model'
import { picErrorKey, usePicMutation } from './usePicMutation'

const { t, locale } = useI18n()
const user = authSession.user
const actor = computed(() => !authSession.signingOut.value && canReadPicRoster(user.value) ? user.value?.id : undefined)
const writable = computed(() => !!actor.value && canWritePicRoster(user.value))
const filter = ref<'all' | 'active' | 'inactive'>('all')
const list = ref<PicPage<Pic> | null>(null)
const listError = ref('')
const loading = ref(false)
const historyId = ref('')
const history = ref<PicPage<PicReceipt> | null>(null)
const historyError = ref('')
const historyLoading = ref(false)
const editor = ref<{ action: PicAction; target: Pic | null; values: PicValues } | null>(null)
const submitted = ref(false)
const editorVisible = ref(false)
const invalidName = computed(() => submitted.value && !!editor.value && normalizePicText(editor.value.values.name) === null)
const invalidDepartment = computed(() => submitted.value && !!editor.value && normalizePicText(editor.value.values.department) === null)
let listGeneration = 0
let historyGeneration = 0

const status = (active: boolean) => t(active ? 'common.active' : 'common.inactive')
const column = (key: string, label = key) => ({ key, dataIndex: key, title: t('picRoster.' + label) })
const columns = computed(() => [column('name'), column('department'), column('isActive', 'status'),
  column('revision'), column('picId'), column('actions')])
const historyColumns = computed(() => [column('revision'), column('action'), column('name'), column('department'),
  column('isActive', 'status'), column('actorUsername', 'actor'), column('requestId'), column('occurredAtUtc', 'occurredAt')])

async function loadList(page = list.value?.page ?? 1): Promise<boolean> {
  if (!actor.value) return false
  const g = ++listGeneration
  loading.value = true
  listError.value = ''
  try {
    const result = await picRosterApi.list(actor.value, page, filter.value === 'all' ? undefined : filter.value === 'active')
    if (g !== listGeneration) return false
    list.value = result
    return true
  } catch (e) {
    if (g === listGeneration) {
      list.value = null
      listError.value = picErrorKey(e)
    }
    return false
  } finally {
    if (g === listGeneration) loading.value = false
  }
}

const { pending, accepted, current, conflict, busy, storageFailed, error: mutationError,
  reset, start, recover, acknowledgeConflict } = usePicMutation({
    actor: () => actor.value, writable: () => writable.value, refresh: () => loadList(),
  })
const newWrite = computed(() => writable.value && !loading.value && !!list.value &&
  !busy.value && !pending.value && !storageFailed.value && !conflict.value)

function refresh() {
  if (accepted.value && pending.value) void recover('receipt')
  else void loadList()
}

function changeFilter(event: { target: unknown }) {
  filter.value = (event.target as { value: typeof filter.value }).value
  list.value = null
  void loadList(1)
}

function openEditor(action: PicAction, target: Pic | null = null) {
  if (!newWrite.value) return
  submitted.value = false
  mutationError.value = ''
  const values = { name: target?.name ?? '', department: target?.department ?? '', isActive: target?.isActive ?? true }
  if (action === 'Activated' || action === 'Deactivated') values.isActive = action === 'Activated'
  editor.value = { action, target, values }
  editorVisible.value = true
}

async function save() {
  const form = editor.value
  if (!form || !newWrite.value) return
  submitted.value = true
  const startedBy = actor.value
  if (invalidName.value || invalidDepartment.value) return
  await start(form.action, form.target, form.values)
  if (startedBy === actor.value && editor.value === form && (pending.value || conflict.value)) editorVisible.value = false
}

function closeEditor() {
  if (busy.value || pending.value || conflict.value) return
  editorVisible.value = false
  editor.value = null
}

function reviewCurrent() {
  const latest = current.value
  if (!latest || !writable.value || pending.value) return
  if (editor.value?.action === 'Updated') {
    editor.value.target = latest
    editor.value.values.isActive = latest.isActive
    editorVisible.value = true
  } else {
    // A new lifecycle decision must be confirmed against the refreshed row.
    editor.value = null
  }
  acknowledgeConflict()
}

async function loadHistory(page: number) {
  if (!actor.value || !historyId.value) return
  const g = ++historyGeneration
  historyLoading.value = true
  historyError.value = ''
  try {
    const result = await picRosterApi.history(actor.value, historyId.value, page)
    if (g === historyGeneration) history.value = result
  } catch (e) {
    if (g === historyGeneration) {
      history.value = null
      historyError.value = picErrorKey(e)
    }
  } finally {
    if (g === historyGeneration) historyLoading.value = false
  }
}

function openHistory(pic: Pic) {
  historyId.value = pic.picId
  history.value = null
  void loadHistory(1)
}

function closeHistory() {
  historyGeneration++
  historyId.value = ''
  history.value = null
  historyLoading.value = false
  historyError.value = ''
}

watch(accepted, receipt => {
  if (receipt) {
    editorVisible.value = false
    editor.value = null
  }
})

watch(actor, () => {
  listGeneration++
  list.value = null
  loading.value = false
  listError.value = ''
  filter.value = 'all'
  editorVisible.value = false
  editor.value = null
  submitted.value = false
  closeHistory()
  reset()
  const saved = pending.value
  if (saved?.action === 'Updated' && saved.picId) {
    editor.value = {
      action: saved.action,
      target: { picId: saved.picId, revision: saved.expectedRevision, ...saved.values },
      values: { ...saved.values },
    }
  }
  if (actor.value) void loadList(1)
}, { immediate: true, flush: 'sync' })

onBeforeUnmount(() => {
  listGeneration++
  closeHistory()
  reset()
})
</script>

<style scoped>
.roster-panel, .roster-fields { display: grid; gap: 14px; }
.roster-toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 10px; }
.roster-toolbar h3, .roster-detail p { margin: 0; }
.roster-toolbar select { padding: 5px 8px; border: 1px solid #aebfc9; border-radius: 4px; }
.roster-detail { display: grid; gap: 10px; padding: 16px; border: 1px solid var(--line); overflow-wrap: anywhere; }
.roster-form { display: grid; gap: 14px; }
.roster-fields { grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr)); }
.roster-fields label { display: grid; align-content: start; gap: 4px; }
.roster-fields label:last-child:has(input[type='checkbox']) { display: flex; align-items: center; }
</style>
