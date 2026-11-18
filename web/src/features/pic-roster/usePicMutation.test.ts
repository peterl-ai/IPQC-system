import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { picRosterApi, PicApiError } from './api'
import { intentKey, readIntent, type Pic, type PicIntent, type PicReceipt } from './model'
import { usePicMutation } from './usePicMutation'

vi.mock('./api', async importOriginal => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, picRosterApi: { mutate: vi.fn(), current: vi.fn(), receipt: vi.fn() } }
})

const actorA = '10000000-0000-0000-0000-000000000001'
const actorB = '10000000-0000-0000-0000-000000000002'
const target: Pic = {
  picId: '20000000-0000-0000-0000-000000000001', revision: 4, name: 'Prior', department: 'Quality', isActive: true,
}
const values = { name: ' Updated ', department: 'QA', isActive: true }
const current = { ...target, revision: 6, name: 'Later' }
const accepted = (intent: PicIntent): PicReceipt => ({
  ...target, ...intent.values, requestId: intent.requestId, revision: intent.expectedRevision + 1,
  action: intent.action, actorUserId: intent.actorId, actorPersonalSessionId: actorB,
  actorUsername: 'test-admin', environmentId: 'test', occurredAtUtc: '2026-07-01T20:00:00Z', contractVersion: 1,
})
let activeActor: string | undefined
let writable: boolean
const refresh = vi.fn<() => Promise<boolean>>()
const setup = () => {
  const state = usePicMutation({ actor: () => activeActor, writable: () => writable, refresh })
  state.reset()
  return state
}

beforeEach(() => {
  vi.resetAllMocks()
  sessionStorage.clear()
  activeActor = actorA
  writable = true
  refresh.mockResolvedValue(true)
})
afterEach(() => vi.restoreAllMocks())

describe('PIC mutation recovery', () => {
  it('retains exactly one request before the first byte is sent and deduplicates double clicks', async () => {
    vi.mocked(picRosterApi.mutate).mockImplementation(async intent => {
      expect(readIntent(actorA)).toEqual(intent)
      throw new TypeError('Connection lost')
    })
    const state = setup()
    await Promise.all([state.start('Created', null, values), state.start('Created', null, values)])
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
    expect(readIntent(actorA)).toMatchObject({ values: { name: 'Updated', department: 'QA' } })
    expect(state.pending.value).toEqual(readIntent(actorA))
    expect(state.error.value).toBe('unavailable')
    expect(state.accepted.value).toBeNull()
  })

  it('preserves a 404 recovery across reload until the original request is accepted', async () => {
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new TypeError('Connection lost'))
    const originalState = setup()
    await originalState.start('Updated', target, values)
    const original = readIntent(actorA)!
    const reloaded = setup()
    vi.mocked(picRosterApi.receipt).mockRejectedValueOnce(new PicApiError(404)).mockResolvedValueOnce(accepted(original))
    vi.mocked(picRosterApi.current).mockResolvedValueOnce(current)
    await reloaded.recover('receipt')
    expect(reloaded.error.value).toBe('receiptMissing')
    expect(reloaded.pending.value).toEqual(original)
    expect(reloaded.accepted.value).toBeNull()
    await reloaded.recover('receipt')
    expect(readIntent(actorA)).toBeNull()
    expect(reloaded.accepted.value).toEqual(accepted(original))
    expect(reloaded.current.value).toEqual(current)
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
  })

  it('only transmits the frozen command on a deliberate retry', async () => {
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new TypeError()).mockImplementationOnce(async intent => accepted(intent))
    vi.mocked(picRosterApi.current).mockResolvedValueOnce({ ...target, revision: 1, name: 'Updated', department: 'QA' })
    const state = setup()
    const draft = { ...values }
    await state.start('Created', null, draft)
    const first = readIntent(actorA)
    draft.name = 'Changed after transmission'
    await state.start('Created', null, draft)
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
    await state.recover('retry')
    expect(vi.mocked(picRosterApi.mutate).mock.calls.map(call => call[0])).toEqual([first, first])
    expect(state.accepted.value?.requestId).toBe(first?.requestId)
    expect(readIntent(actorA)).toBeNull()
  })

  it('requires current-state review after a definitive stale-revision conflict', async () => {
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new PicApiError(409))
    vi.mocked(picRosterApi.current).mockResolvedValueOnce(current)
    vi.mocked(picRosterApi.receipt).mockRejectedValueOnce(new PicApiError(404))
    const state = setup()
    await state.start('Updated', target, values)
    expect(state.conflict.value).toBe(true)
    expect(state.pending.value).toBeNull()
    expect(picRosterApi.current).toHaveBeenCalledBefore(vi.mocked(picRosterApi.receipt))
    expect(state.current.value).toEqual(current)
    await state.start('Updated', current, values)
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
    state.acknowledgeConflict()
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new PicApiError(429))
    await state.start('Updated', current, values)
    const calls = vi.mocked(picRosterApi.mutate).mock.calls
    expect(calls[1]?.[0]).toMatchObject({ expectedRevision: 6, values: { name: 'Updated' } })
    expect(calls[1]?.[0].requestId).not.toBe(calls[0]?.[0].requestId)
  })

  it('keeps an ambiguous create conflict after receipt 404', async () => {
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new PicApiError(409))
    vi.mocked(picRosterApi.receipt).mockRejectedValueOnce(new PicApiError(404))
    const state = setup()
    await state.start('Created', null, values)
    expect(state.conflict.value).toBe(true)
    expect(state.pending.value).toEqual(readIntent(actorA))
    expect(state.accepted.value).toBeNull()
  })

  it.each([401, 403, 429, 503])('retains the original intent after HTTP %i without success or an automatic retry', async status => {
    vi.mocked(picRosterApi.mutate).mockRejectedValueOnce(new PicApiError(status))
    const state = setup()
    await state.start('Created', null, values)
    expect(state.error.value).toBe('error' + status)
    expect(state.pending.value).toEqual(readIntent(actorA))
    expect(state.pending.value).not.toBeNull()
    expect(state.accepted.value).toBeNull()
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
  })

  it('retains a verified receipt until the authoritative refresh completes', async () => {
    vi.mocked(picRosterApi.mutate).mockImplementationOnce(async intent => accepted(intent))
    vi.mocked(picRosterApi.current).mockResolvedValue(current)
    refresh.mockResolvedValueOnce(false)
    const state = setup()
    await state.start('Updated', target, values)
    const original = readIntent(actorA)!
    expect(state.accepted.value).toEqual(accepted(original))
    expect(state.current.value).toEqual(current)
    expect(state.pending.value).toEqual(original)
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
    vi.mocked(picRosterApi.receipt).mockResolvedValueOnce(accepted(original))
    await state.recover('receipt')
    expect(readIntent(actorA)).toBeNull()
  })

  it('keeps recovery data when the current read contradicts the immutable receipt at the same revision', async () => {
    vi.mocked(picRosterApi.mutate).mockImplementationOnce(async intent => accepted(intent))
    vi.mocked(picRosterApi.current).mockResolvedValueOnce({ ...target, revision: 5 })
    const state = setup()
    await state.start('Updated', target, values)
    expect(state.accepted.value?.revision).toBe(5)
    expect(state.current.value).toBeNull()
    expect(state.error.value).toBe('unavailable')
    expect(state.pending.value).toEqual(readIntent(actorA))
    expect(refresh).not.toHaveBeenCalled()
  })

  it('refuses sends with invalid input, corrupted storage or storage write failure', async () => {
    const state = setup()
    await state.start('Created', null, { ...values, department: '  ' })
    expect(state.error.value).toBe('invalidText')
    sessionStorage.setItem(intentKey(actorA), '{broken')
    state.reset()
    await state.start('Created', null, values)
    expect(sessionStorage.getItem(intentKey(actorA))).toBe('{broken')
    expect(state.storageFailed.value).toBe(true)
    sessionStorage.clear()
    state.reset()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementationOnce(() => { throw new Error('Quota') })
    await state.start('Created', null, values)
    expect(state.storageFailed.value).toBe(true)
    expect(picRosterApi.mutate).not.toHaveBeenCalled()
  })

  it('keeps A’s pending intent after account or permission changes and ignores the late response', async () => {
    let resolve!: (receipt: PicReceipt) => void
    vi.mocked(picRosterApi.mutate).mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const state = setup()
    const run = state.start('Created', null, values)
    const original = readIntent(actorA)!
    activeActor = actorB
    await state.recover('retry')
    state.reset()
    resolve(accepted(original))
    await run
    expect(state.accepted.value).toBeNull()
    expect(readIntent(actorA)).toEqual(original)
    expect(readIntent(actorB)).toBeNull()
    activeActor = actorA
    writable = false
    state.reset()
    await state.recover('retry')
    expect(picRosterApi.mutate).toHaveBeenCalledTimes(1)
  })
})
