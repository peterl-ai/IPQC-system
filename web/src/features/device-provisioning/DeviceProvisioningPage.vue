<template>
  <section class="workspace-page">
    <PageHeader :eyebrow="t('nav.system')" :title="t('provisioning.title')" :subtitle="t('provisioning.boundary')" />
    <div class="data-panel">
      <a-alert type="warning" show-icon :message="t('provisioning.boundary')" />
      <a-alert v-if="error" role="alert" type="error" :message="error" />
      <div class="table-toolbar" role="tablist" :aria-label="t('provisioning.title')" @keydown="switchTab">
        <a-button role="tab" :aria-selected="tab === 'lines'" @click="tab = 'lines'">{{ t('provisioning.lines') }}</a-button>
        <a-button role="tab" :aria-selected="tab === 'devices'" @click="tab = 'devices'">{{ t('provisioning.devices') }}</a-button>
        <a-button :loading="loading" @click="load">{{ t('provisioning.refresh') }}</a-button>
        <a-button type="primary" :disabled="loading" @click="open(tab === 'lines' ? 'lineCreate' : 'deviceCreate')">{{ t('provisioning.add') }}</a-button>
      </div>
      <p v-if="loading" role="status">{{ t('provisioning.loading') }}</p>
      <div class="provisioning-scroll" role="tabpanel">
        <table v-if="tab === 'lines'" class="provisioning-table">
          <thead><tr><th>{{ t('provisioning.code') }}</th><th>{{ t('provisioning.name') }}</th><th>{{ t('provisioning.status') }}</th><th>{{ t('provisioning.revision') }}</th><th>{{ t('common.actions') }}</th></tr></thead>
          <tbody>
            <tr v-for="line in lines" :key="line.id">
              <td>{{ line.code }}</td><td>{{ line.name }}</td><td>{{ t(line.isActive ? 'common.active' : 'common.inactive') }}</td><td>{{ line.revision }}</td>
              <td><a-space wrap><a-button @click="open('lineRename', line)">{{ t('common.edit') }}</a-button><a-button @click="open(line.isActive ? 'lineDeactivate' : 'lineActivate', line)">{{ t(line.isActive ? 'provisioning.deactivate' : 'provisioning.activate') }}</a-button><a-button @click="showAudit('production-lines', line.id)">{{ t('provisioning.audit') }}</a-button></a-space></td>
            </tr>
          </tbody>
        </table>
        <table v-else class="provisioning-table">
          <thead><tr><th>{{ t('provisioning.name') }}</th><th>{{ t('provisioning.metadata') }}</th><th>{{ t('provisioning.status') }}</th><th>{{ t('provisioning.reservation') }}</th><th>{{ t('provisioning.revision') }} / {{ t('provisioning.bindingVersion') }}</th><th>{{ t('common.actions') }}</th></tr></thead>
          <tbody>
            <tr v-for="device in devices" :key="device.id">
              <td>{{ device.displayName }}</td><td>{{ device.model || '—' }} / {{ device.serial || '—' }}</td><td>{{ t(`provisioning.state.${device.status}`) }}<div>{{ t('provisioning.notAuthorized') }}</div><div v-if="device.terminalReason">{{ device.terminalReason }}</div></td><td>{{ lineLabel(device.lineId) }}</td><td>{{ device.revision }} / {{ device.bindingVersion }}</td>
              <td>
                <a-space wrap>
                  <template v-if="device.status === 'PendingCredential'">
                    <a-button @click="open('deviceEdit', device)">{{ t('common.edit') }}</a-button>
                    <a-button @click="open(device.lineId ? 'rebind' : 'bind', device)">{{ t(device.lineId ? 'provisioning.rebind' : 'provisioning.bind') }}</a-button>
                    <a-button v-if="device.lineId" @click="open('unbind', device)">{{ t('provisioning.unbind') }}</a-button>
                    <a-button v-if="device.lineId" @click="open('replace', device)">{{ t('provisioning.replace') }}</a-button>
                    <a-button danger @click="open('revoke', device)">{{ t('provisioning.revoke') }}</a-button>
                  </template><a-button @click="showAudit('devices', device.id)">{{ t('provisioning.audit') }}</a-button>
                </a-space>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="!loading && (tab === 'lines' ? lines.length : devices.length) === 0">{{ t('common.noData') }}</p>
      <div class="table-toolbar"><a-button :disabled="loading || currentPage === 1" @click="changePage(-1)">{{ t('provisioning.previous') }}</a-button><span>{{ t('provisioning.page', { page: currentPage, total: tab === 'lines' ? lineTotal : deviceTotal }) }}</span><a-button :disabled="loading || currentPage * 20 >= (tab === 'lines' ? lineTotal : deviceTotal)" @click="changePage(1)">{{ t('provisioning.next') }}</a-button></div>
    </div>

    <a-modal :open="operation !== null" :title="operation ? t(`provisioning.operation.${operation}`) : ''" :ok-text="t('common.confirm')" :cancel-text="t('common.cancel')" :confirm-loading="busy" @ok="save" @cancel="close">
      <p>{{ t('provisioning.boundary') }}</p>
      <p v-if="target">{{ 'code' in target ? target.code : target.displayName }}</p>
      <p v-if="destructive">{{ t('provisioning.confirm') }}</p>
      <a-alert v-if="modalError" role="alert" type="error" :message="modalError" />
      <form class="provisioning-form" @submit.prevent="save">
        <label v-if="operation === 'lineCreate'" for="provision-code">{{ t('provisioning.code') }}<input id="provision-code" v-model="draft.code" maxlength="100" required :disabled="busy"></label>
        <label v-if="editsName" for="provision-name">{{ t('provisioning.name') }}<input id="provision-name" v-model="draft.name" maxlength="200" required :disabled="busy"></label>
        <template v-if="operation === 'deviceCreate' || operation === 'deviceEdit'">
          <label for="provision-model">{{ t('provisioning.model') }}<input id="provision-model" v-model="draft.model" maxlength="200" :disabled="busy"></label>
          <label for="provision-serial">{{ t('provisioning.serial') }}<input id="provision-serial" v-model="draft.serial" maxlength="200" :disabled="busy"></label>
        </template>
        <label v-if="operation === 'bind' || operation === 'rebind'" for="provision-line">{{ t('provisioning.reservation') }}<select id="provision-line" v-model="draft.lineId" required :disabled="busy"><option value="" disabled>{{ t('provisioning.select') }}</option><option v-for="line in lineOptions.filter(x => x.isActive && x.id !== deviceTarget?.lineId)" :key="line.id" :value="line.id">{{ line.code }} — {{ line.name }}</option></select></label>
        <label v-if="operation === 'replace'" for="provision-replacement">{{ t('provisioning.replacement') }}<select id="provision-replacement" v-model="draft.replacementId" required :disabled="busy"><option value="" disabled>{{ t('provisioning.select') }}</option><option v-for="d in deviceOptions.filter(x => x.status === 'PendingCredential' && !x.lineId && x.id !== deviceTarget?.id)" :key="d.id" :value="d.id">{{ d.displayName }}</option></select></label>
        <label v-if="needsReason" for="provision-reason">{{ t('provisioning.reason') }}<textarea id="provision-reason" v-model="draft.reason" maxlength="2000" required :disabled="busy" /></label>
      </form>
    </a-modal>
    <a-drawer :open="auditOpen" :title="t('provisioning.audit')" width="min(800px, 95vw)" @close="auditOpen = false">
      <p v-if="auditLoading" role="status">{{ t('provisioning.loading') }}</p><a-alert v-if="auditError" role="alert" type="error" :message="auditError" />
      <article v-for="entry in auditRows" :key="entry.id" class="audit-entry">
        <h3>{{ t(`provisioning.auditAction.${entry.action}`) }}</h3><p>{{ entry.actorUsername }} · {{ plantDate(entry.occurredAtUtc) }}</p><p v-if="entry.reason">{{ entry.reason }}</p>
        <div class="audit-comparison">
          <section v-for="side in ['before', 'after'] as const" :key="side">
            <strong>{{ t(`provisioning.${side}`) }}</strong><dl v-if="entry[side]">
              <dt>{{ t('provisioning.name') }}</dt><dd>{{ entry[side]!.name }}</dd><dt>{{ t('provisioning.revision') }}</dt><dd>{{ entry[side]!.revision }}</dd>
              <template v-if="entry[side]!.status"><dt>{{ t('provisioning.status') }}</dt><dd>{{ t(`provisioning.state.${entry[side]!.status}`) }}</dd><dt>{{ t('provisioning.reservation') }}</dt><dd>{{ lineLabel(entry[side]!.lineId) }}</dd><dt>{{ t('provisioning.bindingVersion') }}</dt><dd>{{ entry[side]!.bindingVersion }}</dd></template>
              <template v-else><dt>{{ t('provisioning.code') }}</dt><dd>{{ entry[side]!.code }}</dd><dt>{{ t('provisioning.status') }}</dt><dd>{{ t(entry[side]!.isActive ? 'common.active' : 'common.inactive') }}</dd></template>
            </dl><p v-else>—</p>
          </section>
        </div>
      </article>
      <a-button v-if="auditRows.length < auditTotal" :loading="auditLoading" @click="moreAudit">{{ t('provisioning.more') }}</a-button>
    </a-drawer>
  </section>
</template>

<script setup lang="ts">
/* global KeyboardEvent, HTMLElement, HTMLButtonElement */
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/PageHeader.vue'
import { errorCode, provisioningApi, type Audit, type Device, type DeviceAction, type Line } from './api'
const { t } = useI18n()
type Operation = 'lineCreate' | 'lineRename' | 'lineActivate' | 'lineDeactivate' | 'deviceCreate' | 'deviceEdit' | DeviceAction
const tab = ref<'lines' | 'devices'>('lines')
const lines = ref<Line[]>([]), devices = ref<Device[]>([]), lineOptions = ref<Line[]>([]), deviceOptions = ref<Device[]>([])
const linePage = ref(1), devicePage = ref(1), lineTotal = ref(0), deviceTotal = ref(0)
const currentPage = computed(() => tab.value === 'lines' ? linePage.value : devicePage.value)
const loading = ref(false), error = ref(''), busy = ref(false), modalError = ref('')
const operation = ref<Operation | null>(null), target = ref<Line | Device | null>(null)
const deviceTarget = computed(() => target.value && 'displayName' in target.value ? target.value : null)
const lineTarget = computed(() => target.value && 'code' in target.value ? target.value : null)
const editsName = computed(() => ['lineCreate', 'lineRename', 'deviceCreate', 'deviceEdit'].includes(operation.value ?? ''))
const needsReason = computed(() => ['rebind', 'unbind', 'revoke', 'replace'].includes(operation.value ?? ''))
const destructive = computed(() => needsReason.value || operation.value === 'lineDeactivate')
const draft = reactive({ code: '', name: '', model: '', serial: '', lineId: '', replacementId: '', reason: '' })
const auditOpen = ref(false), auditLoading = ref(false), auditError = ref(''), auditRows = ref<Audit[]>([]), auditTotal = ref(0), auditPage = ref(1)
let auditTarget: { kind: 'production-lines' | 'devices'; id: string } | null = null
const describeError = (e: unknown) => t(`provisioning.error.${errorCode(e)}`)
function lineLabel(id?: string | null) { const row = lineOptions.value.find(x => x.id === id); return row?.code ?? id ?? '—' }
function plantDate(value: string) { return new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
async function load() {
  loading.value = true; error.value = ''
  try {
    const [l, d, lo, options] = await Promise.all([provisioningApi.lines(linePage.value), provisioningApi.devices(devicePage.value), provisioningApi.lineOptions(), provisioningApi.deviceOptions()])
    lines.value = l.items; lineTotal.value = l.total; devices.value = d.items; deviceTotal.value = d.total; lineOptions.value = lo; deviceOptions.value = options
  } catch (e) { error.value = describeError(e) } finally { loading.value = false }
}
function changePage(delta: number) { if (tab.value === 'lines') linePage.value += delta; else devicePage.value += delta; void load() }
function switchTab(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key) || (event.target as HTMLElement).getAttribute('role') !== 'tab') return
  event.preventDefault()
  tab.value = event.key === 'Home' ? 'lines' : event.key === 'End' ? 'devices' : tab.value === 'lines' ? 'devices' : 'lines'
  const tabs = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('[role=tab]')
  tabs[tab.value === 'lines' ? 0 : 1]?.focus()
}
function open(action: Operation, row?: Line | Device) {
  operation.value = action; target.value = row ?? null; modalError.value = ''
  Object.assign(draft, { code: '', name: row ? 'code' in row ? row.name : row.displayName : '', model: row && 'model' in row ? row.model ?? '' : '', serial: row && 'serial' in row ? row.serial ?? '' : '', lineId: '', replacementId: '', reason: '' })
}
function close() { if (!busy.value) operation.value = null }
async function save() {
  if (busy.value || !operation.value) return
  if ((editsName.value && !draft.name.trim()) || (operation.value === 'lineCreate' && !draft.code.trim()) || (needsReason.value && !draft.reason.trim()) || (['bind', 'rebind'].includes(operation.value) && !draft.lineId) || (operation.value === 'replace' && !draft.replacementId)) { modalError.value = t('provisioning.error.InvalidInput'); return }
  busy.value = true; modalError.value = ''
  try {
    switch (operation.value) {
      case 'lineCreate': await provisioningApi.createLine(draft.code, draft.name); break
      case 'lineRename': await provisioningApi.renameLine(lineTarget.value!, draft.name); break
      case 'lineActivate': case 'lineDeactivate': await provisioningApi.setLineActive(lineTarget.value!, operation.value === 'lineActivate'); break
      case 'deviceCreate': await provisioningApi.createDevice(draft.name, draft.model, draft.serial); break
      case 'deviceEdit': await provisioningApi.editDevice(deviceTarget.value!, draft.name, draft.model, draft.serial); break
      case 'replace': {
        const replacement = deviceOptions.value.find(x => x.id === draft.replacementId)
        if (!replacement) throw new Error('Missing selection')
        await provisioningApi.deviceAction(deviceTarget.value!, 'replace', { replacementDeviceId: replacement.id, replacementExpectedRevision: replacement.revision, reason: draft.reason }); break
      }
      default: await provisioningApi.deviceAction(deviceTarget.value!, operation.value, { ...(['bind', 'rebind'].includes(operation.value) ? { lineId: draft.lineId } : {}), ...(needsReason.value ? { reason: draft.reason } : {}) })
    }
    operation.value = null; await load()
  } catch (e) { modalError.value = describeError(e); await load() } finally { busy.value = false }
}
async function showAudit(kind: 'production-lines' | 'devices', id: string) {
  auditTarget = { kind, id }; auditLoading.value = false; auditOpen.value = true; auditRows.value = []; auditTotal.value = 0; auditPage.value = 1; await moreAudit()
}
async function moreAudit() {
  if (!auditTarget || auditLoading.value) return
  const selected = auditTarget; auditLoading.value = true; auditError.value = ''
  try { const page = await provisioningApi.audit(selected.kind, selected.id, auditPage.value); if (auditTarget === selected) { auditRows.value.push(...page.items); auditTotal.value = page.total; auditPage.value++ } }
  catch (e) { if (auditTarget === selected) auditError.value = describeError(e) } finally { if (auditTarget === selected) auditLoading.value = false }
}
onMounted(load)
</script>

<style scoped>
.provisioning-scroll { overflow-x: auto; }
.provisioning-table { border-collapse: collapse; width: 100%; min-width: 720px; }
.provisioning-table th, .provisioning-table td { padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: left; vertical-align: top; }
.provisioning-form, .provisioning-form label { display: grid; gap: 8px; }
.provisioning-form { gap: 16px; }
.provisioning-form input, .provisioning-form select, .provisioning-form textarea { width: 100%; padding: 8px; border: 1px solid #9ca3af; border-radius: 6px; }
.audit-entry { padding: 16px 0; border-bottom: 1px solid #e5e7eb; }
.audit-comparison { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.audit-comparison dt { color: #6b7280; }
.audit-comparison dd { margin: 0 0 8px; overflow-wrap: anywhere; }
</style>
