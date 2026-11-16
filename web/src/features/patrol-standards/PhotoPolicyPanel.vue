<template>
  <a-drawer :open="open" :title="t('photoPolicy.title')" width="min(1000px, 94vw)" :mask-closable="false" @close="emit('close')">
    <a-alert v-if="!canRead" type="warning" :message="t('photoPolicy.noAccess')" />
    <template v-else>
      <a-alert type="info" show-icon :message="t('photoPolicy.boundary')" />
      <a-alert v-if="error" role="alert" type="error" show-icon :message="error" />
      <a-alert v-if="conflict" role="status" type="warning" show-icon :message="t('photoPolicy.conflict')" />
      <a-alert v-if="pending" role="status" type="warning" show-icon :message="t('photoPolicy.unknown')" />
      <div v-if="accepted" role="status" class="policy-receipt">
        <strong>{{ t('photoPolicy.accepted') }}</strong>
        <p>{{ t('photoPolicy.receipt') }}: <code>{{ accepted.confirmationId }}</code></p>
        <p>{{ t('photoPolicy.acceptedValue') }}: {{ t(`photoPolicy.${accepted.value}`) }} · {{ time(accepted.confirmedAtUtc) }}</p>
        <a-button :disabled="busy" @click="recoverAccepted">{{ t('photoPolicy.recoverReceipt') }}</a-button>
      </div>
      <div class="policy-toolbar">
        <a-button :loading="busy" :disabled="busy" @click="refresh">{{ t('photoPolicy.refresh') }}</a-button>
        <a-button v-if="canConfirm && pending" :disabled="busy || denied || storageFailed" @click="retry">{{ t('photoPolicy.retry') }}</a-button>
        <a-button v-if="page" :disabled="busy || !!pending" @click="emit('edit')">{{ t('common.edit') }}</a-button>
      </div>
      <template v-if="page?.reviewStandard">
        <h3>{{ page.reviewStandard.name }}</h3>
        <p>{{ t('photoPolicy.standardId') }}: <code>{{ page.standardId }}</code></p>
        <dl class="policy-definition policy-header">
          <template v-for="field in headerFields" :key="field[0]">
            <dt>{{ t(field[1]) }}</dt><dd>{{ page.reviewStandard[field[0]] || t('photoPolicy.empty') }}</dd>
          </template>
        </dl>
        <p>{{ t('photoPolicy.admissionWarning') }}</p>
        <p v-if="!canConfirm">{{ t('photoPolicy.viewOnly') }}</p>
        <a-empty v-if="!page.items.length" :description="t('common.noData')" />
        <article v-for="(item, index) in page.items" :key="item.standardItemId" class="policy-item" :data-item-id="item.standardItemId">
          <h4>{{ index + 1 }}. {{ definition(item.standardItemId)?.inspectionItem || t('photoPolicy.unnamed') }}</h4>
          <p>{{ t('photoPolicy.itemId') }}: <code>{{ item.standardItemId }}</code></p>
          <p><strong>{{ t('photoPolicy.rawText') }}:</strong> {{ item.rawPhotoRequirement || t('photoPolicy.empty') }}</p>
          <p><strong>{{ t('photoPolicy.status') }}:</strong> {{ stateText(item) }}</p>
          <dl v-if="item.confirmation && item.state === 'ExplicitlyConfirmed'" class="policy-definition">
            <dt>{{ t('photoPolicy.confirmedValue') }}</dt><dd>{{ t(`photoPolicy.${item.confirmation.value}`) }}</dd>
            <dt>{{ t('photoPolicy.signer') }}</dt><dd><code>{{ item.confirmation.confirmedByUserId }}</code></dd>
            <dt>{{ t('photoPolicy.confirmedAt') }}</dt><dd>{{ time(item.confirmation.confirmedAtUtc) }}</dd>
            <dt>{{ t('photoPolicy.policyVersion') }}</dt><dd>{{ item.confirmation.policyVersion }}</dd>
            <dt>{{ t('photoPolicy.receipt') }}</dt><dd><code>{{ item.confirmation.confirmationId }}</code></dd>
          </dl>
          <details>
            <summary>{{ t('photoPolicy.reviewDefinition') }}</summary>
            <dl class="policy-definition">
              <dt>{{ t('photoPolicy.definitionVersion') }}</dt><dd>{{ item.definitionVersion ?? t('photoPolicy.unrecorded') }}</dd>
              <dt>{{ t('photoPolicy.definitionId') }}</dt><dd><code>{{ item.definitionId ?? t('photoPolicy.unrecorded') }}</code></dd>
              <dt>{{ t('photoPolicy.definitionHash') }}</dt><dd><code>{{ item.definitionHash }}</code></dd>
              <template v-for="field in itemFields" :key="field[0]">
                <dt>{{ t(field[1]) }}</dt><dd>{{ definition(item.standardItemId)?.[field[0]] || t('photoPolicy.empty') }}</dd>
              </template>
            </dl>
          </details>
          <div v-if="canConfirm" class="policy-controls">
            <label :for="`policy-value-${item.standardItemId}`">{{ t('photoPolicy.choose') }}</label>
            <select :id="`policy-value-${item.standardItemId}`" v-model="choices[item.standardItemId]" :disabled="busy || !!pending || denied || !ready" @change="reviewed[item.standardItemId] = false">
              <option value="">{{ t('photoPolicy.choosePlaceholder') }}</option>
              <option value="Required">{{ t('photoPolicy.Required') }}</option><option value="Optional">{{ t('photoPolicy.Optional') }}</option>
            </select>
            <label class="policy-review-check"><input v-model="reviewed[item.standardItemId]" type="checkbox" :disabled="busy || !!pending || denied || !ready">{{ t('photoPolicy.reviewed') }}</label>
            <a-button type="primary" :disabled="busy || !ready || !!pending || denied || storageFailed || !choices[item.standardItemId] || !reviewed[item.standardItemId] || !knownState(item)" @click="confirm(item)">{{ t('photoPolicy.confirm') }}</a-button>
          </div>
        </article>
      </template>
    </template>
  </a-drawer>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { authSession } from '@/services/authSession'
import { ApiError, patrolStandardsApi } from './api'
import type { PatrolStandard, PatrolStandardItem } from './model'
import { isPolicyValue, matchesReceipt, readIntent, removeIntent, retainIntent, validReview, type PhotoPolicyIntent, type PhotoPolicyItem, type PhotoPolicyPage, type PhotoPolicyReceipt } from './photoPolicyModel'

const props = defineProps<{ standardId: string; open: boolean; refreshVersion: number }>()
const emit = defineEmits<{ close: []; edit: [] }>()
const { t, locale } = useI18n()
const user = authSession.user
const canRead = computed(() => !!authSession.accessToken.value && user.value?.isActive && !user.value.mustChangePassword &&
  ['Admin', 'PQE'].includes(user.value.role) && user.value.permissions.includes('PatrolStandards.Read'))
const canConfirm = computed(() => canRead.value && user.value?.role === 'PQE' && user.value.permissions.includes('PhotoPolicies.Confirm'))
const page = ref<PhotoPolicyPage | null>(null), busy = ref(false), ready = ref(false), error = ref(''), conflict = ref(false)
const pending = ref<PhotoPolicyIntent | null>(null), accepted = ref<PhotoPolicyReceipt | null>(null)
const denied = ref(false), storageFailed = ref(false)
const choices = reactive<Record<string, string>>({}), reviewed = reactive<Record<string, boolean>>({})
let generation = 0
const headerFields: [keyof PatrolStandard, string][] = [['factoryCode', 'field.factoryCode'], ['factoryName', 'field.factoryName'], ['workshopCode', 'field.workshopCode'], ['materialCode', 'field.materialCode']]
const itemFields: [keyof PatrolStandardItem, string][] = [['processCode', 'field.processCode'], ['processName', 'field.processName'], ['itemCategory', 'field.itemCategory'],
  ['inspectionItem', 'field.inspectionItem'], ['inspectionContent', 'field.inspectionContent'], ['upperOperator', 'field.upperOperator'], ['upperValue', 'field.upperValue'],
  ['lowerOperator', 'field.lowerOperator'], ['lowerValue', 'field.lowerValue'], ['inspectionType', 'field.inspectionType'], ['samplingPlan', 'field.samplingPlan'], ['sampleCount', 'field.sampleCount'], ['defectLevel', 'field.defectLevel']]
const definition = (id: string) => page.value?.reviewStandard?.inspectionItems.find(x => x.id === id)
const knownState = (i: PhotoPolicyItem) => ['Unconfirmed', 'ExplicitlyConfirmed', 'InvalidConfiguration'].includes(i.state)
const stateText = (i: PhotoPolicyItem) => t(`photoPolicy.${i.state === 'Unconfirmed' && i.definitionVersion !== null ? 'needsReconfirmation' : knownState(i) ? i.state : 'InvalidConfiguration'}`)
const time = (value: string) => new Date(value).toLocaleString(locale.value === 'en' ? 'en-US' : 'zh-CN', { timeZone: 'America/New_York', timeZoneName: 'short' })
const errorText = (e: unknown) => t(`photoPolicy.${e instanceof ApiError && [400, 401, 403, 404, 409, 429].includes(e.status) ? `error${e.status}` : 'unavailable'}`)

function retire(intent: PhotoPolicyIntent) {
  // Failure to remove recovery state blocks a different request; never silently overwrite it.
  try { removeIntent(intent); pending.value = null }
  catch { storageFailed.value = true; error.value = t('photoPolicy.storageError') }
}
function accept(receipt: PhotoPolicyReceipt, intent: PhotoPolicyIntent) {
  if (!matchesReceipt(receipt, intent)) throw new Error('Mismatched confirmation receipt')
  accepted.value = receipt; retire(intent)
}
async function load(g: number) {
  ready.value = false
  for (const id of Object.keys(reviewed)) reviewed[id] = false
  try {
    const result = await patrolStandardsApi.photoPolicies(props.standardId)
    if (g !== generation) return
    if (!validReview(result, props.standardId)) throw new Error('Coherent review definition unavailable')
    page.value = result
    for (const item of result.items) choices[item.standardItemId] ??= '' // No legacy or confirmed-value preselection.
    const intent = pending.value
    const current = intent && result.items.find(x => x.standardItemId === intent.itemId)?.confirmation
    if (intent && current?.requestId === intent.input.requestId) {
      const receipt = await patrolStandardsApi.photoPolicyReceipt(current.confirmationId)
      if (g !== generation) return
      accept(receipt, intent)
    }
    ready.value = true
  } catch (e) { if (g === generation) { page.value = null; error.value = errorText(e) } }
}
async function refresh() {
  if (busy.value || !canRead.value) return
  busy.value = true; error.value = ''; const g = generation
  await load(g)
  if (g === generation) busy.value = false
}
async function send(intent: PhotoPolicyIntent) {
  if (busy.value || !canConfirm.value || user.value?.id !== intent.actorId || denied.value || storageFailed.value) return
  busy.value = true; error.value = ''; conflict.value = false; const g = generation
  try {
    const receipt = await patrolStandardsApi.confirmPhotoPolicy(intent.standardId, intent.itemId, { ...intent.input })
    if (g !== generation) return
    accept(receipt, intent)
    // An accepted historical receipt is not proof that it is still the current applicable policy.
    await load(g)
  } catch (e) {
    if (g !== generation) return
    error.value = errorText(e)
    if (e instanceof ApiError && [400, 409].includes(e.status)) {
      retire(intent); conflict.value = e.status === 409
      await load(g) // Retain intended choice; require review + a deliberate new action/new RequestId.
    } else if (e instanceof ApiError && [401, 403, 404].includes(e.status)) {
      denied.value = true; ready.value = false // Keep unknown original for the same actor's later authorized recovery.
    }
    // Network/5xx/parse failure is unknown: keep the exact persisted request, never declare success.
  } finally { if (g === generation) busy.value = false }
}
async function confirm(item: PhotoPolicyItem) {
  const value = choices[item.standardItemId], actor = user.value
  if (!actor || !canConfirm.value || !ready.value || busy.value || pending.value || denied.value || storageFailed.value || !knownState(item) || !reviewed[item.standardItemId] || !isPolicyValue(value)) return
  accepted.value = null // A previous request's receipt cannot label this new request as accepted.
  const intent: PhotoPolicyIntent = { actorId: actor.id, standardId: props.standardId, itemId: item.standardItemId, input: {
    requestId: crypto.randomUUID(), canonicalizationVersion: 1, expectedDefinitionId: item.definitionId,
    expectedDefinitionHash: item.definitionHash, expectedConfirmationId: item.confirmation?.confirmationId ?? null, value,
  } }
  try { retainIntent(intent); pending.value = intent }
  catch { storageFailed.value = true; error.value = t('photoPolicy.storageError'); return }
  await send(intent)
}
async function retry() { if (pending.value) await send(pending.value) }
async function recoverAccepted() {
  const receipt = accepted.value
  if (!receipt || busy.value || !canRead.value) return
  busy.value = true; const g = generation
  try {
    const original = await patrolStandardsApi.photoPolicyReceipt(receipt.confirmationId)
    if (g !== generation) return
    if (JSON.stringify(original) !== JSON.stringify(receipt)) throw new Error('Receipt changed')
    error.value = ''; await load(g)
  } catch (e) { if (g === generation) error.value = errorText(e) }
  finally { if (g === generation) busy.value = false }
}
watch(() => [props.standardId, props.open, props.refreshVersion, user.value?.id, canRead.value, canConfirm.value], async () => {
  const g = ++generation
  busy.value = false; ready.value = false; page.value = null; error.value = ''; conflict.value = false; accepted.value = null; pending.value = null; denied.value = false; storageFailed.value = false
  for (const key of Object.keys(choices)) delete choices[key]
  for (const key of Object.keys(reviewed)) delete reviewed[key]
  if (!props.open || !canRead.value || !user.value) return
  try {
    pending.value = readIntent(user.value.id, props.standardId)
    if (pending.value) choices[pending.value.itemId] = pending.value.input.value
  } catch { storageFailed.value = true; error.value = t('photoPolicy.storageError') }
  busy.value = true; await load(g); if (g === generation) busy.value = false
}, { immediate: true })
onBeforeUnmount(() => { generation++ })
</script>

<style scoped>
.policy-toolbar { display: flex; gap: 10px; margin: 16px 0; }
.policy-item, .policy-receipt { padding: 18px; margin: 16px 0; background: #fff; border: 1px solid var(--line); }
.policy-item h4 { margin: 0 0 10px; font-size: 16px; }
.policy-item p, .policy-receipt p { overflow-wrap: anywhere; white-space: pre-wrap; }
.policy-definition { display: grid; grid-template-columns: minmax(150px, 1fr) 3fr; gap: 8px 16px; }
.policy-definition dt { color: var(--ink-600); }
.policy-definition dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.policy-controls { display: grid; gap: 12px; margin-top: 18px; max-width: 650px; }
.policy-controls select { width: 240px; padding: 7px; border: 1px solid #aebfc9; border-radius: 4px; background: #fff; }
.policy-review-check { display: flex; align-items: flex-start; gap: 8px; }
details summary { cursor: pointer; color: var(--blue-600); }
</style>
