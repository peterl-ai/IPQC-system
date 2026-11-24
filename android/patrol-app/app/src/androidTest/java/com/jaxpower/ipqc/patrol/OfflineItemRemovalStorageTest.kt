package com.jaxpower.ipqc.patrol

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.work.*
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflineItemRemovalStorageTest {
    @Test fun realRoomAndKeystorePreserveAThroughOfflineRestartAndAuthorizedBHandoff() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            val grant = activate(a, IDENTITY)
            val scope = fixture.scope()
            val attempt = fixture.attempt(scope.taskId, 1)
            val local = a.online.offlineItemRemovals
            val intent = local.newRemoval(scope, IDENTITY, attempt, true, RemovalConfirmation.Production, SECRET_REASON)
            val removal = local.save(intent)
            val calls = fixture.wire.reads.get()
            val original = requireNotNull(grant.source)
            assertEquals(original, removal.source)
            assertEquals("1.25", local.draft(scope, IDENTITY).samples.single().value.inspectionValue)
            assertEquals(calls, fixture.wire.reads.get())

            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val restarted = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                val reopened = restarted.online.offlineItemRemovals
                assertEquals(removal, reopened.draft(scope, IDENTITY).history.single().removal)
                assertEquals(removal, reopened.save(intent))
                assertEquals(calls, fixture.wire.reads.get())
                restarted.sessions.logout(attemptRemote = false)
                assertNull(fixture.device.vault.read())
                assertFailure(Issue.UNAUTHENTICATED) { reopened.draft(scope, IDENTITY) }

                fixture.device.api.loginSubject = fixture.b
                val b = activate(restarted, fixture.b)
                assertTrue(reopened.draft(scope, fixture.b).history.isEmpty())
                assertWorkFailure(WorkProblem.SourceUnavailable) { reopened.save(intent) }
                assertWorkFailure(WorkProblem.OperationIdentityConflict) {
                    val replacement = reopened.newRemoval(scope, fixture.b, fixture.attempt(scope.taskId, 0),
                        true, RemovalConfirmation.Equipment)
                    reopened.save(replacement.copy(operationId = intent.operationId))
                }
                val other = reopened.save(reopened.newRemoval(scope, fixture.b, fixture.attempt(scope.taskId, 0),
                    true, RemovalConfirmation.Equipment))
                assertEquals(fixture.b.userId, other.source.contributorUserId)
                assertEquals(fixture.b.familyId, other.source.originPersonalSessionId)
                assertNotEquals(original.contextId, other.source.contextId)

                assertWorkFailure(WorkProblem.SourceUnavailable) {
                    restarted.store.prepareItemRemovalForReconciliation(b.access, b.draft.sampleBasis, benchId(81), removal.reference())
                }
                fixture.wire.explicitRemovalSource = original
                val authorized = restarted.online.preflight(b, intent.operationId)
                assertEquals(1, fixture.wire.removalPreflights.get())
                val batch = restarted.online.prepareItemRemovalForReconciliation(authorized, benchId(81), removal.reference())
                restarted.store.startItemRemovalAttempt(authorized.access, authorized.draft.sampleBasis, batch.batchId)
                restarted.store.markItemRemovalOutcomeUnknown(authorized.access, batch.batchId)
                val bAccess = fixture.b
                val outcome = ItemRemovalReceipt(scope.taskId, intent.operationId, batch.batchId, "RemoveItem", 1,
                    removal.contentHash, attempt.taskItemId, attempt.inspectionAttemptId, original.contextId,
                    IDENTITY.userId, bAccess.userId, bAccess.familyId, scope.environmentId, scope.lineId,
                    scope.workflowVersion, NOW, 8, ItemDisposition.Removed, true, intent.confirmation, intent.reason,
                    removal.minimumSampleCount, 1, false)
                val receipt = ItemRemovalBatchReceipt(scope.taskId, batch.batchId, batch.fingerprint, bAccess.userId,
                    bAccess.familyId, NOW, 7, 8, emptyList(), listOf(outcome), 2)
                fixture.wire.explicitRemovalReceipt = receipt
                assertEquals(ItemRemovalReceiptRecovery.BatchConfirmed(receipt), restarted.online.recoverItemRemovalReceipt(
                    authorized, ReceiptLookup(ReceiptKind.Batch, batch.batchId)))
                assertEquals(1, fixture.wire.removalReceiptReads.get())
                val history = reopened.draft(scope, bAccess).history
                val accepted = history.single { it.state == WorkState.Accepted }
                assertEquals(removal, accepted.removal)
                assertEquals(IDENTITY.userId, accepted.receipt?.contributorUserId)
                assertEquals(bAccess.userId, accepted.receipt?.uploadedByUserId)
                assertEquals(0, fixture.wire.sends.get())
                assertEncrypted(fixture.device.databaseFile)
            } finally {
                db.close()
            }
        }
    }

    @Test fun roomRollbackAndLostAcknowledgementKeepOneCommandAndBatchAcrossReopen() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = RemovalFaultRows(fixture.device.rows)
            val a = fixture.workServices(rows = faults)
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val command = local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Equipment, SECRET_REASON)
            val count = fixture.device.rows.count()
            faults.rollback.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(count, fixture.device.rows.count())
            assertTrue(local.draft(task, IDENTITY).history.isEmpty())
            faults.dropAck.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(1L, local.draft(task, IDENTITY).localEditVersion)
            val saved = local.save(command)
            val batchId = benchId(83)
            faults.rollback.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.prepareBatch(task, IDENTITY, batchId, saved.reference()) }
            assertNull(local.draft(task, IDENTITY).history.single().batch)
            faults.dropAck.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.prepareBatch(task, IDENTITY, batchId, saved.reference()) }
            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val fresh = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db)).online.offlineItemRemovals
                assertEquals(saved, fresh.save(command))
                val sealed = fresh.prepareBatch(task, IDENTITY, batchId, saved.reference())
                assertEquals(sealed, fresh.draft(task, IDENTITY).history.single().batch)
                assertEquals(1L, fresh.draft(task, IDENTITY).localEditVersion)
                assertEquals(0, fixture.wire.sends.get())
                assertEncrypted(fixture.device.databaseFile)
            } finally {
                db.close()
            }
        }
    }

    @Test fun realProtectedClockRejectsExpiredReceiptsAndRollbackWithoutErasingTheItem() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val saved = local.save(local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Production))
            val pending = local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 1),
                true, RemovalConfirmation.Equipment)
            val first = fixture.device.sample
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + 60_000, utcMs = first.utcMs + 60_000)
            assertWorkFailure(WorkProblem.CaptureWindowClosed) { local.save(pending) }
            assertEquals(saved, local.save(saved.command))
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + 60_001, utcMs = first.utcMs - 180_000)
            assertFailure(Issue.OFFLINE_CLOCK) { local.draft(task, IDENTITY) }
            assertEquals(saved, a.rows.transaction(task) { itemRemoval(saved.command.operationId)?.removal })
        }
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val saved = local.save(local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Production))
            val first = fixture.device.sample
            val expires = 72 * 3600 * 1000L
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + expires, utcMs = first.utcMs + expires)
            assertFailure(Issue.OFFLINE_EXPIRED) { local.save(saved.command) }
            assertEquals(saved, a.rows.transaction(task) { itemRemoval(saved.command.operationId)?.removal })
        }
    }

    private suspend fun activate(app: WorkbenchWorkServices, owner: SessionIdentity):
        OnlineWorkCoordinator.Grant {
        app.sessions.login("synthetic-inspector", "test-only")
        val line = app.patrol.lines().single { it.id == LINE.id }
        val selected = app.patrol.tasks(line, 1).items.single()
        val grant = app.online.issueOrReuse(app.online.open(selected, owner))
        app.online.armOfflineSamples(grant)
        return grant
    }

    private suspend fun assertWorkFailure(problem: WorkProblem, run: suspend () -> Any?) {
        val failure = runCatching { run() }.exceptionOrNull()
        assertEquals(problem, (failure as? WorkFailure)?.problem)
    }

    private suspend fun assertFailure(issue: Issue, run: suspend () -> Any?) {
        val failure = runCatching { run() }.exceptionOrNull()
        assertEquals(issue, (failure as? PatrolFailure)?.issue)
    }

    private fun assertEncrypted(database: File) {
        database.parentFile!!.listFiles().orEmpty().filter { it.name.startsWith(database.name) }.forEach {
            assertFalse(it.readBytes().toString(Charsets.ISO_8859_1).contains(SECRET_REASON))
        }
    }

    private class RemovalFaultRows(private val base: CacheRows) : CacheRows by base {
        val rollback = AtomicBoolean()
        val dropAck = AtomicBoolean()
        private val writes = AtomicInteger()

        override suspend fun replace(slot: String, sealed: ByteArray) {
            base.replace(slot, sealed)
            writes.incrementAndGet()
            if (slot.startsWith("work-v1:") && rollback.compareAndSet(true, false)) error("Synthetic rollback")
        }

        override suspend fun <T> transaction(block: suspend () -> T): T {
            val before = writes.get()
            val result = base.transaction(block)
            if (writes.get() != before && dropAck.compareAndSet(true, false)) error("Synthetic lost acknowledgement")
            return result
        }
    }

    private companion object { const val SECRET_REASON = "Synthetic private disposition remark" }
}
