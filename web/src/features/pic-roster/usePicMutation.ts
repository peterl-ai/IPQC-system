import { ref } from 'vue'
import { picRosterApi, PicApiError } from './api'
import {
  normalizePicText, readIntent, releaseIntent, requireRetainedIntent, retainIntent,
  type Pic, type PicAction, type PicIntent, type PicReceipt, type PicValues,
} from './model'

export function picErrorKey(error: unknown): string {
  const status = error instanceof PicApiError ? error.status : 0
  return [400, 401, 403, 404, 409, 413, 429, 503].includes(status) ? 'error' + status : 'unavailable'
}

export function usePicMutation(context: {
  actor: () => string | undefined
  writable: () => boolean
  refresh: () => Promise<boolean>
}) {
  const pending = ref<PicIntent | null>(null)
  const accepted = ref<PicReceipt | null>(null)
  const current = ref<Pic | null>(null)
  const conflict = ref(false)
  const busy = ref(false)
  const storageFailed = ref(false)
  const error = ref('')
  let generation = 0

  function reset() {
    generation++
    busy.value = false
    pending.value = accepted.value = current.value = null
    storageFailed.value = conflict.value = false
    error.value = ''
    const actor = context.actor()
    if (!actor) return
    try {
      pending.value = readIntent(actor)
    } catch {
      failStorage()
    }
  }

  function failStorage() {
    storageFailed.value = true
    error.value = 'storageError'
  }

  function retire(intent: PicIntent) {
    try {
      releaseIntent(intent)
      pending.value = null
    } catch {
      failStorage()
    }
  }

  async function reconcile(receipt: PicReceipt, intent: PicIntent, g: number) {
    accepted.value = receipt
    conflict.value = false
    error.value = ''
    // Keep recovery state until the current record and roster are both refreshed.
    const latest = await picRosterApi.current(intent.actorId, receipt.picId)
    if (g !== generation) return
    if (latest.revision < receipt.revision ||
      (latest.revision === receipt.revision &&
        (latest.name !== receipt.name || latest.department !== receipt.department || latest.isActive !== receipt.isActive))) {
      throw new Error('Current PIC does not agree with the accepted revision')
    }
    current.value = latest
    if (await context.refresh() && g === generation) retire(intent)
  }

  async function reviewConflict(intent: PicIntent, g: number) {
    if (intent.picId) {
      const latest = await picRosterApi.current(intent.actorId, intent.picId)
      if (g !== generation) return
      current.value = latest
    }
    try {
      const receipt = await picRosterApi.receipt(intent)
      if (g === generation) await reconcile(receipt, intent, g)
    } catch (e) {
      if (g !== generation) return
      if (!(e instanceof PicApiError && e.status === 404)) throw e

      // A 404 alone cannot retire an operation. Read the current revision first
      // and prove this frozen command can no longer write against that revision.
      const target = current.value
      if (target && (target.revision > intent.expectedRevision ||
        (target.revision === intent.expectedRevision &&
          (intent.action === 'Updated'
            ? target.name === intent.values.name && target.department === intent.values.department
            : target.isActive === intent.values.isActive)))) {
        retire(intent)
      }
      await context.refresh()
    }
  }

  async function run(intent: PicIntent, mode: 'new' | 'retry' | 'receipt') {
    if (busy.value || context.actor() !== intent.actorId ||
        (mode !== 'receipt' && (!context.writable() || storageFailed.value))) return
    if (mode !== 'receipt') {
      try {
        requireRetainedIntent(intent)
      } catch {
        failStorage()
        return
      }
    }
    const g = generation
    busy.value = true
    error.value = ''
    try {
      let receipt: PicReceipt
      try {
        receipt = await (mode === 'receipt' ? picRosterApi.receipt(intent) : picRosterApi.mutate(intent))
      } catch (e) {
        if (g !== generation) return
        error.value = mode === 'receipt' && e instanceof PicApiError && e.status === 404
          ? 'receiptMissing' : picErrorKey(e)
        if (e instanceof PicApiError && mode !== 'receipt') {
          if (e.status === 409) {
            conflict.value = true
            current.value = null
            await reviewConflict(intent, g)
          } else if (mode === 'new' && [400, 404, 413].includes(e.status)) {
            // An explicit validation/missing-target rejection on the first send.
            retire(intent)
          }
        }
        return
      }
      if (g === generation) await reconcile(receipt, intent, g)
    } catch (e) {
      if (g === generation) error.value = picErrorKey(e)
    } finally {
      if (g === generation) busy.value = false
    }
  }

  async function start(action: PicAction, target: Pic | null, values: PicValues) {
    const actorId = context.actor()
    if (!actorId || !context.writable() || busy.value || pending.value || storageFailed.value || conflict.value) return
    const name = normalizePicText(values.name)
    const department = normalizePicText(values.department)
    if (name === null || department === null) {
      error.value = 'invalidText'
      return
    }
    const intent: PicIntent = {
      actorId, action, picId: target?.picId ?? null, expectedRevision: target?.revision ?? 0,
      requestId: crypto.randomUUID(), values: { name, department, isActive: values.isActive },
    }
    accepted.value = current.value = null
    try {
      retainIntent(intent)
      pending.value = intent
    } catch {
      failStorage()
      return
    }
    await run(intent, 'new')
  }

  function recover(mode: 'retry' | 'receipt') {
    if (pending.value) return run(pending.value, mode)
  }

  function acknowledgeConflict() {
    if (!pending.value && !busy.value && current.value && !storageFailed.value) {
      conflict.value = false
      error.value = ''
    }
  }

  return { pending, accepted, current, conflict, busy, storageFailed, error, reset, start, recover, acknowledgeConflict }
}
