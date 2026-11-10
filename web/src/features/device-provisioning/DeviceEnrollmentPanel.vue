<template>
  <a-drawer :open="true" :title="t('enrollment.title')" width="min(860px, 95vw)" @close="emit('close')">
    <a-alert type="warning" show-icon :message="t('enrollment.boundary')" />
    <p>{{ t('provisioning.deviceId') }}: {{ device.id }}</p>
    <a-alert v-if="error" role="alert" type="error" :message="error" />
    <div class="table-toolbar">
      <a-button :disabled="busy" @click="refresh">{{ t('provisioning.refresh') }}</a-button>
    </div>
    <p v-if="busy" role="status">{{ t('provisioning.loading') }}</p>
    <article v-for="row in rows" :key="row.id" class="enrollment-entry">
      <h3>{{ t(`enrollment.state.${row.state}`) }}</h3>
      <dl>
        <dt>{{ t('enrollment.id') }}</dt><dd>{{ row.id }}</dd><dt>{{ t('enrollment.environment') }}</dt><dd>{{ row.environment }}</dd>
        <dt>{{ t('provisioning.reservation') }}</dt><dd>{{ row.lineId }}</dd><dt>{{ t('enrollment.versions') }}</dt><dd>{{ row.sourceBindingVersion }} → {{ row.targetBindingVersion }}</dd>
        <dt>{{ t('enrollment.expires') }}</dt><dd>{{ date(row.expiresAtUtc) }}</dd>
        <template v-if="row.spkiSha256"><dt>{{ t('enrollment.spki') }}</dt><dd>{{ row.spkiSha256 }}</dd><dt>{{ t('enrollment.csr') }}</dt><dd>{{ row.csrSha256 }}</dd></template>
      </dl>
      <p v-if="row.terminalReason">{{ row.terminalReason }}</p>
      <template v-if="row.candidate">
        <h4>{{ t('enrollment.candidate') }}: {{ t(`enrollment.credential.${row.candidate.state}`) }}</h4>
        <dl><dt>{{ t('enrollment.credentialId') }}</dt><dd>{{ row.candidate.id }}</dd><dt>{{ t('enrollment.operationId') }}</dt><dd>{{ row.candidate.issuanceOperationId }}</dd><dt>{{ t('enrollment.certHash') }}</dt><dd>{{ row.candidate.certificateSha256 || '—' }}</dd><dt>{{ t('enrollment.validUntil') }}</dt><dd>{{ date(row.candidate.notAfterUtc) }}</dd></dl>
        <p v-if="row.candidate.externalRevocationPending">{{ t('enrollment.revocationPending') }}</p>
        <p v-if="row.candidate.lastErrorCode">{{ t('enrollment.issuerRecovery') }}</p>
      </template>
      <form v-if="row.state !== 'Cancelled'" class="enrollment-cancel" @submit.prevent="cancel(row)">
        <label :for="`cancel-${row.id}`">{{ t('provisioning.reason') }}</label><textarea :id="`cancel-${row.id}`" v-model="reasons[row.id]" maxlength="2000" :disabled="busy" />
        <a-button danger :disabled="busy || !reasons[row.id]?.trim()" @click="cancel(row)">{{ t('enrollment.cancel') }}</a-button>
      </form>
    </article>
    <p v-if="!busy && rows.length === 0">{{ t('common.noData') }}</p>
    <a-button v-if="rows.length < total" :disabled="busy" @click="loadMore">{{ t('provisioning.more') }}</a-button>
  </a-drawer>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { errorCode, ProvisioningApiError, type Device } from './api'
import { enrollmentApi, type Enrollment } from './enrollmentApi'
const props = defineProps<{ device: Device }>()
const emit = defineEmits<{ close: []; changed: [] }>()
const { t } = useI18n()
const rows = ref<Enrollment[]>([]), total = ref(0), page = ref(1), busy = ref(false), error = ref('')
const reasons = reactive<Record<string, string>>({})
function date(value: string) { return new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
function describe(e: unknown) {
  if (e instanceof ProvisioningApiError && e.status === 409) return t('provisioning.error.Conflict')
  if (e instanceof ProvisioningApiError && e.status === 429) return t('enrollment.rateLimited')
  return t(`provisioning.error.${errorCode(e)}`)
}
async function load(reset: boolean) {
  const next = reset ? 1 : page.value
  const result = await enrollmentApi.list(props.device.id, next)
  rows.value = reset ? result.items : [...rows.value, ...result.items]; total.value = result.total; page.value = next + 1
}
async function run(action: () => Promise<void>) {
  if (busy.value) return
  busy.value = true; error.value = ''
  try { await action() } catch (e) { error.value = describe(e) } finally { busy.value = false }
}
function refresh() { return run(() => load(true)) }
function loadMore() { return run(() => load(false)) }
function cancel(row: Enrollment) {
  const reason = reasons[row.id]?.trim()
  if (!reason) return
  return run(async () => { await enrollmentApi.cancel(row.id, row.revision, reason); delete reasons[row.id]; emit('changed'); await load(true) })
}
onMounted(refresh)
</script>

<style scoped>
.enrollment-entry, .enrollment-secret { padding: 16px 0; border-bottom: 1px solid #d1d5db; }
dd { margin: 0 0 10px; overflow-wrap: anywhere; }
dt { color: #6b7280; }
.enrollment-approval, .enrollment-cancel { display: grid; gap: 10px; margin: 16px 0; }
textarea { min-height: 60px; }
</style>
