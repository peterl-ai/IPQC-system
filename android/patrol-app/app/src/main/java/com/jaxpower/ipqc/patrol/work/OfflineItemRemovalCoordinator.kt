package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.util.UUID

// Local capture and sealing have no transport. The command pins the original
// scope at formation and can be kept for an exact retry after a lost acknowledgement.
internal class OfflineItemRemovalCoordinator(private val store: SharedWorkStore) {
    suspend fun newRemoval(task: WorkTask, owner: SessionIdentity, attempt: WorkAttempt,
                           confirmedAbsentOrNotApplicable: Boolean, confirmation: RemovalConfirmation,
                           reason: String? = null): ItemRemovalCommand =
        store.newItemRemoval(task, owner, attempt, UUID.randomUUID().toString(),
            confirmedAbsentOrNotApplicable, confirmation, reason)

    suspend fun save(command: ItemRemovalCommand): ItemRemovalEnvelope = store.saveItemRemoval(command)

    suspend fun draft(task: WorkTask, owner: SessionIdentity): LocalItemDispositionDraft =
        store.itemDispositionDraft(task, owner)

    suspend fun prepareBatch(task: WorkTask, owner: SessionIdentity, batchId: String,
                             operation: OperationReference): ItemRemovalBatch =
        store.prepareItemRemovalBatch(task, owner, batchId, operation)
}
