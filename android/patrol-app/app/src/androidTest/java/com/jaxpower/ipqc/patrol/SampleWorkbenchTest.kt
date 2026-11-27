package com.jaxpower.ipqc.patrol

import android.graphics.Bitmap
import android.view.KeyEvent
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW as FIXTURE_NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.SECOND_LINE
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.ui.Load
import com.jaxpower.ipqc.patrol.ui.PatrolApp
import com.jaxpower.ipqc.patrol.ui.Screen
import com.jaxpower.ipqc.patrol.work.*
import java.io.File
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SampleWorkbenchTest {
    @get:Rule val compose = createComposeRule()

    @Test fun publicBuildFlagMatchesTheSignedArtifactExpectation() {
        InstrumentationRegistry.getArguments().getString("ipqcWorkbenchEnabled")?.let { expected ->
            assertEquals(expected.toBooleanStrict(), BuildConfig.IPQC_ENABLE_SAMPLE_WORKBENCH)
        }
        if (!BuildConfig.DEBUG) assertFalse(BuildConfig.IPQC_ENABLE_SAMPLE_WORKBENCH)
    }

    @Test fun disabledAppKeepsCachedTaskReadOnlyWithoutAnyWorkRequests() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components(enabled = false)
            install(app)
            login(app)
            openDetail(fixture, app)
            compose.onNodeWithText("Open sample workbench").assertDoesNotExist()
            compose.onNodeWithText("Remove item").assertDoesNotExist()
            assertEquals(0, fixture.wire.reads.get())
            assertEquals(0, fixture.wire.issues.get())
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun realComposePilotPreservesTypedValuesAndHistoryAcrossOfflineSaveCorrectionAndRemoval() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            val size = install(app)
            login(app)
            openWorkbench(fixture, app)
            assertEquals(0, fixture.wire.issues.get())
            choose(fixture, 1)
            pane().performScrollToNode(hasText("1.25 · OK"))
            compose.onNodeWithText("1.25 · OK").assertIsDisplayed()
            compose.onNodeWithText("Server accepted").assertIsDisplayed()

            activate(app)
            offline(app)
            val reads = fixture.wire.reads.get()
            val details = fixture.detailCalls.get()
            val issued = fixture.wire.issues.get()
            choose(fixture, 0)
            pane().performScrollToNode(hasText("OK") and hasClickAction())
            compose.onNodeWithTag("inspection-value").assertDoesNotExist()
            compose.onNodeWithText("OK").performClick()
            saveSample()
            awaitCount(app, 1)
            compose.onNodeWithText("Saved locally · Pending sync").assertIsDisplayed()
            val first = draft(app).history.single().operation.envelope
            assertEquals("OK", first.operation.data?.judgment)
            assertEquals(1, draft(app).progress.first { it.attempt == first.operation.attempt }.required)
            pane().performScrollToNode(hasText("Correct sample") and hasClickAction())
            button("Correct sample").performClick()
            pane().performScrollToNode(hasText("NG") and hasClickAction())
            button("NG").performClick()
            saveSample()
            awaitCount(app, 2)
            val corrected = draft(app).history.last().operation.envelope
            assertEquals("NG", corrected.operation.data?.judgment)
            assertEquals(listOf(first.reference()), corrected.operation.dependencies)

            pane().performScrollToNode(hasText("Remove sample") and hasClickAction())
            button("Remove sample").performClick()
            compose.onNodeWithText("Remove this sample?").assertIsDisplayed()
            assertEquals(2, draft(app).history.size)
            button("Cancel").performClick()
            button("Remove sample").performClick()
            button("Confirm removal").performClick()
            awaitCount(app, 3)
            val removed = draft(app).history.last().operation.envelope
            assertEquals(SampleKind.RemoveSample, removed.operation.kind)
            assertNull(removed.operation.data)
            assertEquals(first.operation.clientSampleId, removed.operation.clientSampleId)
            assertEquals(0, draft(app).progress.single { it.attempt == first.operation.attempt }.applicableSamples)
            val rejected = runBlocking {
                runCatching {
                    app.online.offlineSamples.save(fixture.scope(), OfflineSampleEdit.Correct(
                        LocalSampleKey(corrected.operation.attempt, corrected.operation.clientSampleId),
                        SampleInput.Qualitative(QualitativeJudgment.OK), corrected.reference()), IDENTITY)
                }.exceptionOrNull()
            }
            assertEquals(WorkProblem.DependencyMismatch, (rejected as WorkFailure).problem)
            pane().performScrollToNode(hasText("Sample removed"))
            button("Correct sample").assertDoesNotExist()

            compose.runOnIdle { size.value = DpSize(620.dp, 910.dp) }
            choose(fixture, 1)
            val exact = "9007199254740993.000000000001"
            pane().performScrollToNode(hasTestTag("inspection-value"))
            compose.onNodeWithTag("inspection-value").performTextInput("1e-40")
            saveSample()
            compose.onNodeWithText("Enter a valid decimal", substring = true).assertIsDisplayed()
            assertEquals(3, draft(app).history.size)
            pane().performScrollToNode(hasTestTag("inspection-value"))
            compose.onNodeWithTag("inspection-value").performTextReplacement(exact)
            pane().performScrollToNode(hasTestTag("inspection-barcode"))
            compose.onNodeWithTag("inspection-barcode").performTextInput("SYNTHETIC-17")
            saveSample()
            awaitCount(app, 4)
            val quantitative = draft(app).history.last().operation.envelope.operation
            assertEquals(exact, quantitative.data?.inspectionValue)
            assertEquals("SYNTHETIC-17", quantitative.data?.barcode)
            assertNull(quantitative.data?.judgment)
            assertEquals(50, draft(app).progress.single { it.attempt == quantitative.attempt }.required)
            assertEquals(2, draft(app).progress.single { it.attempt == quantitative.attempt }.applicableSamples)
            assertEquals(reads, fixture.wire.reads.get())
            assertEquals(details, fixture.detailCalls.get())
            assertEquals(issued, fixture.wire.issues.get())
            assertEquals(0, fixture.wire.sends.get())
            compose.onNodeWithText("Submit", substring = true).assertDoesNotExist()

            pane().performScrollToIndex(0)
            capture("portrait")
            compose.runOnIdle { size.value = DpSize(1100.dp, 760.dp) }
            compose.onNodeWithText("Development Pilot / Local Draft").assertIsDisplayed()
            pane().performScrollToIndex(0)
            capture("landscape")
            close(app)
        }
    }

    @Test fun exactLocalRetryAndLookupSurviveRoomRollbackAndLostAcknowledgement() {
        WorkbenchDeviceFixture().use { fixture ->
            val fault = FaultRows(fixture.device.rows)
            val app = fixture.components(rows = fault)
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            offline(app)

            fault.mode.set(Fault.Before)
            chooseQualitative("OK")
            saveSample()
            awaitRetry(app)
            assertEquals(0, runBlocking { app.online.offlineSamples.draft(fixture.scope(), IDENTITY).history.size })
            compose.onNodeWithText("Saved locally", substring = true).assertDoesNotExist()
            button("Check local save").performClick()
            compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.error?.startsWith("No local save found") == true }
            button("Retry same save").performClick()
            awaitCount(app, 1)

            fault.mode.set(Fault.After)
            chooseQualitative("NG")
            saveSample()
            awaitRetry(app)
            val durable = runBlocking { app.online.offlineSamples.draft(fixture.scope(), IDENTITY) }
            assertEquals(2, durable.history.size)
            compose.onNodeWithText("Saved locally", substring = true).assertDoesNotExist()
            button("Check local save").performClick()
            awaitCount(app, 2)
            assertEquals(durable.history.last().operation.envelope.operation.operationId,
                draft(app).history.last().operation.envelope.operation.operationId)
            compose.onNodeWithText("Saved locally · Pending sync").assertIsDisplayed()
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun pendingSaveBlocksSystemBackToolbarBackAndSwitchLineUntilExactAcknowledgement() {
        WorkbenchDeviceFixture().use { fixture ->
            val fault = FaultRows(fixture.device.rows)
            val app = fixture.components(rows = fault)
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            offline(app)

            fault.mode.set(Fault.Before)
            chooseQualitative("OK")
            saveSample()
            awaitRetry(app)
            compose.onNodeWithText("Back").assertIsNotEnabled()
            compose.onNodeWithText("Switch line").assertIsNotEnabled()
            InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_BACK)
            compose.runOnIdle {
                assertTrue(app.model.ui.value.screen is Screen.SampleWorkbench)
                // Exercise stale click callbacks as well as disabled semantics.
                app.model.back()
                app.model.switchLine()
                assertTrue(app.model.ui.value.screen is Screen.SampleWorkbench)
                assertTrue(app.model.workbench?.ui?.value?.retryPending == true)
            }
            button("Retry same save").performClick()
            awaitCount(app, 1)
            compose.onNodeWithText("Back").assertIsEnabled()
            compose.onNodeWithText("Switch line").assertIsEnabled()
            assertEquals(1L, draft(app).localEditVersion)
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun captureDeadlineLocksEditingWhileValidReceiptStillShowsRetainedWork() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            offline(app)
            chooseQualitative("OK")
            saveSample()
            awaitCount(app, 1)
            advance(fixture, 60_000)
            compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.blocked == WorkProblem.CaptureWindowClosed }
            pane().performScrollToNode(hasText("Save locally"))
            compose.onNodeWithText("Save locally").assertIsNotEnabled()
            assertEquals(1, draft(app).history.size)
            assertEquals(0, fixture.wire.sends.get())
            advance(fixture, 72 * 3600 * 1000L - 60_000)
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login }
            assertNull(app.model.workbench?.ui?.value?.draft)
            compose.onNodeWithTag("sample-workbench").assertDoesNotExist()
            assertEquals(1L, runBlocking { app.rows.transaction(fixture.scope()) { catalog().localEditVersion } })
            close(app)
        }
    }

    @Test fun clockAnomalyAfterArmingClearsProtectedProjection() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            chooseQualitative("OK")
            saveSample()
            awaitCount(app, 1)
            fixture.device.sample = fixture.device.sample.copy(boot = -1)
            compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.draft == null }
            compose.onNodeWithText("Local capture active").assertDoesNotExist()
            compose.onNodeWithText("Saved locally · Pending sync").assertDoesNotExist()
            assertEquals(1L, runBlocking { app.rows.transaction(fixture.scope()) { catalog().localEditVersion } })
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun recreatedProcessAndSecondOperatorAndLineKeepExactScopes() {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.components()
            val current = mutableStateOf(a)
            compose.setContent {
                DeviceConfigurationOverride(DeviceConfigurationOverride.ForcedSize(DpSize(1000.dp, 710.dp))) {
                    val app = current.value
                    key(app) { PatrolApp(app.model) }
                }
            }
            login(a)
            openWorkbench(fixture, a)
            activate(a)
            offline(a)
            chooseQualitative("OK")
            saveSample()
            awaitCount(a, 1)
            val old = draft(a).history.single().operation.envelope
            close(a)
            fixture.device.database.close()

            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val app = fixture.components(MutableStateFlow(false), fixture.device.openCache(db), RoomCacheRows(db))
                compose.runOnIdle { current.value = app }
                openWorkbench(fixture, app)
                awaitCount(app, 1)
                assertEquals(old.contentHash, draft(app).history.single().operation.envelope.contentHash)
                advance(fixture, 60_000)
                compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.blocked == WorkProblem.CaptureWindowClosed }
                button("Sign out").performClick()
                compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login && !app.model.ui.value.busy }
                fixture.device.api.loginSubject = fixture.b
                app.connectivity.value = true
                login(app)
                openWorkbench(fixture, app)
                assertTrue(draft(app).history.isEmpty())
                activate(app)

                val oldRouteCommand = app.online.offlineSamples.newCapture(
                    fixture.attempt(fixture.task().id, 0), SampleInput.Qualitative(QualitativeJudgment.OK))
                val stale = runBlocking {
                    runCatching { app.online.offlineSamples.save(fixture.scope(), oldRouteCommand, IDENTITY) }
                        .exceptionOrNull()
                }
                assertEquals(WorkProblem.SourceUnavailable, (stale as WorkFailure).problem)
                chooseQualitative("NG")
                saveSample()
                awaitCount(app, 1)
                assertEquals(fixture.b.userId, draft(app).history.single().operation.envelope.source.contributorUserId)
                assertEquals(fixture.b.familyId, draft(app).history.single().operation.envelope.source.originPersonalSessionId)
                val persistedA = runBlocking { app.rows.transaction(fixture.scope()) { operation(old.operation.operationId) } }
                assertEquals(IDENTITY.userId, persistedA?.envelope?.source?.contributorUserId)
                assertEquals(old.contentHash, persistedA?.envelope?.contentHash)
                button("Switch line").performClick()
                openFromLines(fixture, app, SECOND_LINE)
                enterWorkbench(app)
                assertEquals(fixture.secondTaskId, app.model.workbench?.ui?.value?.target?.task?.taskId)
                assertEquals(SECOND_LINE.id, app.model.workbench?.ui?.value?.target?.task?.lineId)
                assertTrue(draft(app).history.isEmpty())
                activate(app)
                chooseQualitative("OK")
                saveSample()
                awaitCount(app, 1)
                assertEquals(SECOND_LINE.id, draft(app).history.single().operation.envelope.source.task.lineId)
                assertEquals(LINE.id, persistedA?.envelope?.source?.task?.lineId)
                assertEquals(0, fixture.wire.sends.get())
                close(app)
            } finally {
                db.close()
            }
        }
    }

    @Test fun logoutDuringRoomWriteClearsWorkbenchAndRollsBackUnacknowledgedOperation() {
        WorkbenchDeviceFixture().use { fixture ->
            val fault = FaultRows(fixture.device.rows)
            val app = fixture.components(rows = fault)
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            offline(app)
            fault.mode.set(Fault.Hold)
            chooseQualitative("OK")
            saveSample()
            runBlocking { withTimeout(20_000) { fault.entered.await() } }
            try {
                button("Sign out").performClick()
            } finally {
                fault.release.complete(Unit)
            }
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login && !app.model.ui.value.busy }
            assertNull(app.model.workbench?.ui?.value?.draft)
            compose.onNodeWithText("Saved locally", substring = true).assertDoesNotExist()
            assertEquals(0L, runBlocking { app.rows.transaction(fixture.scope()) { catalog().localEditVersion } })
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun protectedBatchStatusesAreShownWithoutTurningThemIntoEditableSamples() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            val states = listOf(WorkState.InFlight, WorkState.OutcomeUnknown, WorkState.Conflict, WorkState.Quarantined)
            runBlocking {
                val ledger = app.online.offlineSamples
                val attempt = fixture.attempt(fixture.task().id, 0)
                val ids = states.mapIndexed { index, _ ->
                    val edit = ledger.newCapture(attempt, SampleInput.Qualitative(QualitativeJudgment.OK))
                    ledger.save(fixture.scope(), edit, IDENTITY)
                    ledger.prepareBatch(fixture.scope(), benchId(70 + index), listOf(edit.operationId)).batchId
                }
                // Seed previously persisted outcomes for this UI-only surface.
                app.rows.transaction(fixture.scope()) {
                    ids.zip(states).forEach { (id, state) ->
                        val old = requireNotNull(batch(id))
                        save(old.copy(state = state))
                    }
                }
            }
            button("Reload draft").performClick()
            awaitCount(app, 4)
            assertEquals(states, draft(app).history.map { it.operation.state })
            assertTrue(draft(app).effective.all { it.editablePredecessor == null })
            val labels = listOf("Captured · In flight", "Captured · Outcome unknown", "Captured · Conflict", "Captured · Quarantined")
            labels.forEach {
                pane().performScrollToNode(hasText(it))
                compose.onNodeWithText(it).assertIsDisplayed()
            }
            button("Correct sample").assertDoesNotExist()
            assertEquals(0, fixture.wire.sends.get())
            close(app)
        }
    }

    @Test fun itemRemovalConfirmationRequiresBothInputsAndKeepsSavedDispositionLocal() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            compose.onNodeWithTag("remove-inspection-item").assertDoesNotExist()
            activate(app)
            openRemovalDialog()
            button("Save removal").assertIsNotEnabled()
            button("Cancel").performClick()
            assertEquals(0L, runBlocking { app.rows.transaction(fixture.scope()) { catalog().localEditVersion } })
            assertEquals(0, fixture.wire.removalPosts.get())
            assertEquals(0, fixture.wire.removalPreflights.get())

            openRemovalDialog()
            button("Equipment").performClick()
            button("Save removal").assertIsNotEnabled()
            compose.onNodeWithTag("confirm-item-removal").performClick()
            val reason = compose.onNodeWithTag("item-removal-reason")
            reason.performTextInput("x".repeat(501))
            button("Save removal").assertIsNotEnabled()
            reason.performTextClearance()
            button("Save removal").assertIsEnabled().performClick()
            val row = awaitRemoval(app, WorkState.Queued)
            val saved = runBlocking { app.rows.transaction(fixture.scope()) { itemRemoval(row.operation.operationId) } }
            assertEquals(RemovalConfirmation.Equipment, saved?.removal?.command?.confirmation)
            assertEquals("", saved?.removal?.command?.reason)
            assertNull(saved?.batch)
            removalStatus("Removal saved locally").assertIsDisplayed()
            removalStatus("Server-confirmed Removed").assertDoesNotExist()
            compose.onNodeWithTag("remove-inspection-item").assertDoesNotExist()
            pane().performScrollToNode(hasText("Save locally") and hasClickAction())
            button("Save locally").assertIsNotEnabled()

            choose(fixture, 1)
            pane().performScrollToNode(hasTestTag("inspection-value"))
            compose.onNodeWithTag("inspection-value").performTextInput("1.5")
            saveSample()
            awaitCount(app, 1)
            assertEquals(0, fixture.wire.removalPosts.get())
            assertEquals(0, fixture.wire.removalPreflights.get())
            close(app)
        }
    }

    @Test fun ambiguousRemovalSaveUsesExactCommandAndReopenKeepsOneOriginal() {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = FaultRows(fixture.device.rows)
            val app = fixture.components(rows = faults)
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            confirmRemoval("Production")
            faults.mode.set(Fault.After)
            button("Save removal").performClick()
            compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.removalSavePending == true }
            val scope = fixture.scope()
            val initial = requireNotNull(runBlocking {
                app.rows.transaction(scope) { itemRemoval(catalog().itemRemovalIds.single()) }
            })
            assertNull(initial.removal.command.reason)
            val oldTarget = requireNotNull(app.model.workbench?.ui?.value?.target)
            compose.runOnIdle { app.model.switchLine() }
            assertTrue(app.model.ui.value.screen is Screen.SampleWorkbench)
            button("Retry same removal save").performClick()
            val row = awaitRemoval(app, WorkState.Queued)
            assertEquals(initial.removal.reference(), row.operation)
            assertEquals(1L, runBlocking { app.rows.transaction(scope) { catalog().localEditVersion } })
            assertEquals(0, fixture.wire.removalPosts.get())

            close(app)
            compose.runOnIdle { app.model.checkWorkbenchRemovalSave(oldTarget) }
            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val reopened = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                val restored = runBlocking {
                    reopened.sessions.login("fixture-inspector", "test-only")
                    reopened.online.offlineItemRemovals.draft(scope, IDENTITY)
                }
                assertEquals(initial.removal, restored.history.single().removal)
            } finally {
                db.close()
            }
        }
    }

    @Test fun operatorBExplicitlyUploadsAAndRapidCallbacksCannotDuplicatePost() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            val size = install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            confirmRemoval("Production")
            button("Save removal").performClick()
            val a = awaitRemoval(app, WorkState.Queued)
            val original = requireNotNull(runBlocking {
                app.rows.transaction(fixture.scope()) { itemRemoval(a.operation.operationId) }
            }).removal
            assertNull(original.command.reason)
            offline(app)
            app.connectivity.value = true
            compose.waitUntil(20_000) { app.model.ui.value.online }
            compose.runOnIdle { size.value = DpSize(680.dp, 910.dp) }
            assertEquals(0, fixture.wire.removalPosts.get())
            assertEquals(0, fixture.wire.removalPreflights.get())
            compose.runOnIdle { app.model.logout() }
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login && !app.model.ui.value.busy }
            fixture.device.api.loginSubject = fixture.b
            login(app)
            openWorkbench(fixture, app)
            val b = awaitRemoval(app, WorkState.Queued)
            assertEquals(a.operation, b.operation)
            compose.onNodeWithTag("remove-inspection-item").assertDoesNotExist()
            assertEquals(0, fixture.wire.removalPosts.get())

            fixture.wire.explicitRemovalSource = original.source
            fixture.wire.removalUploadTestEnabled = true
            fixture.wire.removalResponse = { body ->
                val sealed = requireNotNull(app.rows.transaction(fixture.scope()) { itemRemoval(b.operation.operationId) })
                assertEquals(WorkState.InFlight, sealed.state)
                val accepted = removalAcceptance(sealed, fixture.b, remaining = 1)
                assertEquals(JsonPrimitive(original.command.owner.userId), body.getValue("operation").jsonObject["contributorUserIdAssertion"])
                fixture.wire.observedRemoval = accepted.itemOperations.single()
                removalReply(accepted)
            }
            val target = requireNotNull(app.model.workbench?.ui?.value?.target)
            compose.runOnIdle {
                app.model.uploadWorkbenchItemRemoval(target, b)
                app.model.uploadWorkbenchItemRemoval(target, b)
            }
            awaitRemoval(app, WorkState.Accepted)
            val accepted = requireNotNull(runBlocking {
                app.rows.transaction(fixture.scope()) { itemRemoval(b.operation.operationId) }
            })
            assertEquals(original, accepted.removal)
            assertEquals(IDENTITY.userId, accepted.serverEvidence?.contributorUserId)
            assertEquals(fixture.b.userId, accepted.serverEvidence?.uploadedByUserId)
            assertEquals(fixture.b.familyId, accepted.serverEvidence?.uploadedPersonalSessionId)
            assertEquals(1, fixture.wire.removalPosts.get())
            assertEquals(1, fixture.wire.removalPreflights.get())
            removalStatus("Server-confirmed Removed").assertIsDisplayed()
            close(app)
        }
    }

    @Test fun rateLimitedPresentationAfterUnknownUploadRequiresReloadAndExactRecoveryForB() =
        verifyRateLimitedRemovalPresentation(WorkState.OutcomeUnknown)

    @Test fun rateLimitedPresentationAfterAcceptedUploadKeepsTheDurableReceipt() =
        verifyRateLimitedRemovalPresentation(WorkState.Accepted)

    @Test fun rateLimitedPresentationAfterDefinitiveConflictKeepsTheOriginal() =
        verifyRateLimitedRemovalPresentation(WorkState.Conflict)

    private fun verifyRateLimitedRemovalPresentation(expected: WorkState) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            val size = install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            confirmRemoval("Production")
            button("Save removal").performClick()
            val task = fixture.scope()
            val queued = awaitRemoval(app, WorkState.Queued)
            val id = queued.operation.operationId
            fun stored() = requireNotNull(runBlocking { app.rows.transaction(task) { itemRemoval(id) } })
            val original = stored().removal
            val sampleHistory = runBlocking { app.online.offlineItemRemovals.draft(task, IDENTITY).samples }
            compose.runOnIdle { app.model.logout() }
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login && !app.model.ui.value.busy }
            fixture.device.api.loginSubject = fixture.b
            login(app)
            openWorkbench(fixture, app)
            val before = awaitRemoval(app, WorkState.Queued)
            assertFalse(requireNotNull(app.model.workbench?.ui?.value).hasLocalAccess)
            fixture.wire.explicitRemovalSource = original.source
            fixture.wire.removalUploadTestEnabled = true
            fixture.wire.afterRemovalPost = { fixture.wire.sharedDraftRateLimits.incrementAndGet() }
            if (expected == WorkState.Conflict) {
                fixture.wire.removalProblem = 409 to """{"status":409,"title":"DraftVersionConflict","code":"DraftVersionConflict"}"""
            }
            fixture.wire.removalResponse = {
                if (expected == WorkState.OutcomeUnknown) throw java.io.IOException("Synthetic disconnect after POST")
                val receipt = removalAcceptance(stored(), fixture.b, remaining = 1)
                fixture.wire.observedRemoval = receipt.itemOperations.single()
                removalReply(receipt)
            }
            button("Upload removal").performClick()
            awaitRemovalRefreshFailure(app)
            assertEquals(expected, stored().state)
            assertEquals(0, fixture.wire.sharedDraftRateLimits.get())
            assertTrue(app.model.ui.value.online)
            if (compose.onAllNodes(hasText("Upload removal") and hasClickAction()).fetchSemanticsNodes().isNotEmpty())
                button("Upload removal").assertIsNotEnabled()
            val target = requireNotNull(app.model.workbench?.ui?.value?.target)
            val reads = fixture.detailCalls.get()
            compose.runOnIdle { app.model.uploadWorkbenchItemRemoval(target, before) }
            compose.waitForIdle()
            assertEquals(reads, fixture.detailCalls.get())
            assertEquals(1, fixture.wire.removalPosts.get())
            compose.onNodeWithText("Reload the draft to verify removal status.", substring = true).assertIsDisplayed()
            if (expected == WorkState.Accepted)
                compose.onNodeWithText("Server confirmed the item removal.", substring = true).assertIsDisplayed()
            if (expected == WorkState.Conflict)
                compose.onNodeWithText("The removal was rejected.", substring = true).assertIsDisplayed()
            if (expected == WorkState.OutcomeUnknown) captureRemovalLayouts(size, "p2-refresh-required")

            button("Reload draft").performClick()
            val after = awaitRemoval(app, expected)
            assertEquals(before.operation, after.operation)
            val batch = requireNotNull(stored().batch)
            assertEquals(batch.batchId, after.batchId)
            if (expected == WorkState.OutcomeUnknown) {
                captureRemovalLayouts(size, "p2-recovery-restored")
                fixture.wire.missingRemovalReceipt = true
                fixture.wire.missingIndividualRemovalReceipt = true
                button("Check removal receipt").performClick()
                awaitRemoval(app, WorkState.OutcomeUnknown)
                assertEquals(WorkState.OutcomeUnknown, stored().state)
                val receipt = removalAcceptance(stored(), fixture.b, remaining = 1)
                fixture.wire.missingIndividualRemovalReceipt = false
                fixture.wire.explicitIndividualRemovalReceipt = receipt.itemOperations.single()
                button("Check removal receipt").performClick()
                awaitRemoval(app, WorkState.OutcomeUnknown, confirmed = true)
                assertEquals(WorkState.OutcomeUnknown, stored().state)
                fixture.wire.missingRemovalReceipt = false
                fixture.wire.explicitRemovalReceipt = receipt
                fixture.wire.observedRemoval = receipt.itemOperations.single()
                fixture.wire.afterFullReceipt = { fixture.wire.sharedDraftRateLimits.incrementAndGet() }
                button("Check removal receipt").performClick()
                awaitRemovalRefreshFailure(app)
                assertEquals(WorkState.Accepted, stored().state)
                assertEquals(receipt, stored().batchReceipt)
                compose.onNodeWithText("Server confirmed the item removal and its batch.").assertIsDisplayed()
                button("Reload draft").performClick()
                awaitRemoval(app, WorkState.Accepted)
            }
            assertEquals(original, stored().removal)
            assertEquals(batch, stored().batch)
            assertEquals(sampleHistory, requireNotNull(app.model.workbench?.ui?.value?.draft).confirmed)
            assertEquals(1, fixture.wire.removalPosts.get())
            close(app)
        }
    }

    private fun awaitRemovalRefreshFailure(app: WorkbenchComponents) {
        compose.waitUntil(20_000) {
            val state = app.model.workbench?.ui?.value
            state != null && !state.busy && state.error?.contains("Too many requests") == true
        }
    }

    @Test fun activationReplacesOlderApplicableProjectionWithNewerRemoteRemoval() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            val size = install(app)
            login(app)
            openWorkbench(fixture, app)
            val task = fixture.scope()
            val removed = fixture.attempt(task.taskId, 0)
            val before = requireNotNull(app.model.workbench?.ui?.value)
            assertEquals(7L, before.draft?.observedServerDraftVersion)
            assertEquals(ItemDisposition.Applicable, before.removalView(true)?.items?.single { it.attempt == removed }?.server)
            fixture.wire.observedRemoval = ItemRemovalReceipt(task.taskId, benchId(81), benchId(82), "RemoveItem", 1,
                "A".repeat(64), removed.taskItemId, removed.inspectionAttemptId, benchId(83), IDENTITY.userId, IDENTITY.userId,
                IDENTITY.familyId, task.environmentId, task.lineId, task.workflowVersion, FIXTURE_NOW, 8,
                ItemDisposition.Removed, true, RemovalConfirmation.Equipment, null, 1, 1, false)
            activate(app)
            val current = requireNotNull(app.model.workbench?.ui?.value)
            assertEquals(8L, current.draft?.observedServerDraftVersion)
            val view = requireNotNull(current.removalView(true))
            assertEquals(ItemDisposition.Removed, view.items.single { it.attempt == removed }.server)
            assertEquals(1, view.items.count { it.server == ItemDisposition.Applicable })
            assertFalse(current.canStartRemoval(removed))
            assertTrue(current.draft?.progress.orEmpty().none { it.attempt == removed })
            compose.onNodeWithTag("remove-inspection-item").assertDoesNotExist()
            removalStatus("Server-confirmed Removed").assertIsDisplayed()
            captureRemovalLayouts(size, "p2-activation-current")
            close(app)
        }
    }

    @Test fun receiptRecoveryKeepsUnknownThrough404AndIndividualAndShowsLastItemInBothLayouts() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            val size = install(app)
            val task = fixture.scope()
            val other = fixture.attempt(task.taskId, 1)
            fixture.wire.priorObservedRemoval = ItemRemovalReceipt(task.taskId, benchId(81), benchId(82), "RemoveItem", 1,
                "A".repeat(64), other.taskItemId, other.inspectionAttemptId, benchId(83), IDENTITY.userId, IDENTITY.userId,
                IDENTITY.familyId, task.environmentId, task.lineId, task.workflowVersion, FIXTURE_NOW, 7,
                ItemDisposition.Removed, true, RemovalConfirmation.Equipment, null, 50, 1, false)
            login(app)
            openWorkbench(fixture, app)
            captureRemovalLayouts(size, "read-only")
            activate(app)
            captureRemovalLayouts(size, "eligible")
            captureRemovalConfirmationLayouts(size)
            openRemovalDialog()
            compose.onNodeWithText("This is the last applicable item.", substring = true).assertIsDisplayed()
            confirmRemoval("Equipment")
            button("Save removal").performClick()
            val row = awaitRemoval(app, WorkState.Queued)
            removalStatus("Local projection: the last applicable item is queued for removal.").assertIsDisplayed()
            captureRemovalLayouts(size, "queued")
            val original = requireNotNull(runBlocking { app.rows.transaction(task) { itemRemoval(row.operation.operationId) } })
            fixture.wire.explicitRemovalSource = original.removal.source
            fixture.wire.removalUploadTestEnabled = true
            fixture.wire.removalResponse = { throw java.io.IOException("Synthetic disconnect after the POST") }
            pane().performScrollToNode(hasText("Upload removal") and hasClickAction())
            button("Upload removal").performClick()
            val unknown = awaitRemoval(app, WorkState.OutcomeUnknown)
            assertNotNull(unknown.batchId)
            button("Upload removal").assertDoesNotExist()
            captureRemovalLayouts(size, "outcome-unknown")

            fixture.wire.missingRemovalReceipt = true
            fixture.wire.missingIndividualRemovalReceipt = true
            pane().performScrollToNode(hasText("Check removal receipt") and hasClickAction())
            button("Check removal receipt").performClick()
            awaitRemoval(app, WorkState.OutcomeUnknown)
            compose.onNodeWithText("Receipt not found yet.", substring = true).assertIsDisplayed()

            val attempted = requireNotNull(runBlocking { app.rows.transaction(task) { itemRemoval(row.operation.operationId) } })
            val receipt = removalAcceptance(attempted, IDENTITY, remaining = 0)
            fixture.wire.missingIndividualRemovalReceipt = false
            fixture.wire.explicitIndividualRemovalReceipt = receipt.itemOperations.single()
            button("Check removal receipt").performClick()
            awaitRemoval(app, WorkState.OutcomeUnknown, confirmed = true)
            compose.onNodeWithText("Online refresh required").assertIsDisplayed()
            captureRemovalLayouts(size, "stale")
            assertEquals(1, fixture.wire.removalPosts.get())

            fixture.wire.explicitRemovalReceipt = receipt
            fixture.wire.missingRemovalReceipt = false
            fixture.wire.observedRemoval = receipt.itemOperations.single()
            pane().performScrollToNode(hasText("Check removal receipt") and hasClickAction())
            button("Check removal receipt").performClick()
            awaitRemoval(app, WorkState.Accepted)
            removalStatus("Server-confirmed Removed").assertIsDisplayed()
            removalStatus("Server confirmed that no applicable items remained", prefix = true).assertIsDisplayed()
            captureRemovalLayouts(size, "confirmed-last")
            compose.onNodeWithText("Submit", substring = false).assertDoesNotExist()
            assertEquals(1, fixture.wire.removalPosts.get())
            assertEquals(original.removal,
                runBlocking { app.rows.transaction(task) { itemRemoval(row.operation.operationId)?.removal } })
            close(app)
        }
    }

    @Test fun oldItemConfirmationCannotSaveAfterNavigationAndLogoutRemovesDialog() {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            val old = requireNotNull(app.model.workbench?.ui?.value?.target)
            val attempt = fixture.attempt(old.task.taskId, 0)
            compose.runOnIdle { app.model.back() }
            compose.waitUntil(20_000) { app.model.ui.value.screen is Screen.Detail }
            enterWorkbench(app)
            compose.runOnIdle {
                app.model.saveWorkbenchItemRemoval(old, attempt, true, RemovalConfirmation.Equipment, "obsolete callback")
            }
            assertEquals(0L, runBlocking { app.rows.transaction(fixture.scope()) { catalog().localEditVersion } })
            openRemovalDialog()
            compose.runOnIdle { app.model.logout() }
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login && !app.model.ui.value.busy }
            compose.onNodeWithTag("item-removal-confirmation").assertDoesNotExist()
            assertEquals(0, fixture.wire.removalPosts.get())
        }
    }

    @Test fun backgroundCancelsUncommittedRemovalAndLineSwitchCannotRedirectIt() {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = FaultRows(fixture.device.rows)
            val app = fixture.components(rows = faults)
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            confirmRemoval("Equipment")
            faults.mode.set(Fault.Hold)
            button("Save removal").performClick()
            runBlocking { withTimeout(20_000) { faults.entered.await() } }
            compose.runOnIdle {
                app.model.switchLine()
                assertTrue(app.model.ui.value.screen is Screen.SampleWorkbench)
                app.model.leaveForeground()
            }
            faults.release.complete(Unit)
            compose.waitUntil(20_000) { app.model.ui.value.screen == Screen.Login }
            val saved = runBlocking { app.rows.transaction(fixture.scope()) { catalog().itemRemovalIds } }
            assertTrue(requireNotNull(saved).isEmpty())
            assertEquals(0, fixture.wire.removalPosts.get())
        }
    }

    @Test fun definitiveRemovalConflictShowsThePreservedOriginal() =
        removalRejectionIsVisible(WorkState.Conflict, "Removal conflict · Original kept")

    @Test fun confirmedSourceRevocationShowsQuarantineAndDisablesUpload() =
        removalRejectionIsVisible(WorkState.Quarantined, "Removal quarantined · Source no longer eligible")

    private fun removalRejectionIsVisible(state: WorkState, label: String) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.components()
            install(app)
            login(app)
            openWorkbench(fixture, app)
            activate(app)
            openRemovalDialog()
            confirmRemoval("Production")
            button("Save removal").performClick()
            val row = awaitRemoval(app, WorkState.Queued)
            val task = fixture.scope()
            val original = requireNotNull(runBlocking {
                app.rows.transaction(task) { itemRemoval(row.operation.operationId) }
            }).removal
            fixture.wire.explicitRemovalSource = original.source
            fixture.wire.removalUploadTestEnabled = true
            val code = if (state == WorkState.Conflict) "DraftVersionConflict" else "SourceSecurityRevoked"
            val fields = if (state == WorkState.Conflict) "" else
                ""","preserveOriginals":true,"quarantineRequired":true,
                   "sourceContextIds":["${original.source.contextId}"],"operationIds":["${row.operation.operationId}"]"""
            fixture.wire.removalProblem = 409 to """{"status":409,"title":"$code","code":"$code"$fields}"""
            pane().performScrollToNode(hasText("Upload removal") and hasClickAction())
            button("Upload removal").performClick()
            awaitRemoval(app, state)
            removalStatus(label).assertIsDisplayed()
            button("Upload removal").assertDoesNotExist()
            assertEquals(original, runBlocking { app.rows.transaction(task) { itemRemoval(row.operation.operationId)?.removal } })
            assertEquals(1, fixture.wire.removalPosts.get())
            close(app)
        }
    }

    private fun openRemovalDialog() {
        pane().performScrollToNode(hasTestTag("remove-inspection-item"))
        compose.onNodeWithTag("remove-inspection-item").performClick()
        compose.onNodeWithText("Remove inspection item?").assertIsDisplayed()
    }

    private fun confirmRemoval(source: String) {
        compose.onNodeWithTag("confirm-item-removal").performClick()
        button(source).performClick()
    }

    private fun removalStatus(label: String, prefix: Boolean = false) =
        compose.onNode(hasText(label, substring = prefix) and hasAnyAncestor(hasTestTag("item-removal-status")))

    private fun awaitRemoval(app: WorkbenchComponents, state: WorkState, confirmed: Boolean? = null): WorkbenchRemoval.Row {
        fun current() = app.model.workbench?.ui?.value?.takeUnless { it.busy }?.removalView(online = true)?.rows
            ?.singleOrNull { it.state == state && (confirmed == null || it.operationConfirmed == confirmed) }
        compose.waitUntil(20_000) { current() != null }
        return requireNotNull(current())
    }

    private fun removalAcceptance(entry: ItemRemovalEntry, uploader: SessionIdentity, remaining: Int): ItemRemovalBatchReceipt {
        val batch = requireNotNull(entry.batch)
        val saved = entry.removal
        val cmd = saved.command
        val task = cmd.task
        val placed = ItemRemovalReceipt(task.taskId, cmd.operationId, batch.batchId, "RemoveItem", 1, saved.contentHash,
            cmd.attempt.taskItemId, cmd.attempt.inspectionAttemptId, cmd.contextId, cmd.owner.userId, uploader.userId,
            uploader.familyId, task.environmentId, task.lineId, task.workflowVersion, FIXTURE_NOW, cmd.expectedDraftVersion + 1,
            ItemDisposition.Removed, true, cmd.confirmation, cmd.reason, saved.minimumSampleCount, remaining, remaining == 0)
        return ItemRemovalBatchReceipt(task.taskId, batch.batchId, batch.fingerprint, uploader.userId, uploader.familyId,
            FIXTURE_NOW, cmd.expectedDraftVersion, placed.draftVersion, emptyList(), listOf(placed), 2)
    }

    private fun removalReply(accepted: ItemRemovalBatchReceipt): String =
        """{"batchReceipt":${Json.encodeToString(accepted)},"operation":${Json.encodeToString(accepted.itemOperations.single())},
            "replayOnly":false,"canExecute":false,"acceptsFormalInspection":false}"""

    private fun captureRemovalLayouts(size: MutableState<DpSize>, name: String) {
        captureLayouts(size, name) { compose.onNodeWithTag("sample-workbench").captureToImage().asAndroidBitmap() }
    }

    private fun captureRemovalConfirmationLayouts(size: MutableState<DpSize>) {
        captureLayouts(size, "confirmation") {
            // Open after applying that layout. Changing the host viewport can
            // dispose a modal window while its content is being captured.
            openRemovalDialog()
            compose.waitForIdle()
            val image = checkNotNull(InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot())
            button("Cancel").performClick()
            image
        }
    }

    private fun captureLayouts(size: MutableState<DpSize>, name: String, capture: () -> Bitmap) {
        if (InstrumentationRegistry.getArguments().getString("ipqcWorkbenchScreenshots") != "true") return
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val dir = File(context.cacheDir, "item-removal-evidence").apply { mkdirs() }
        for ((layout, viewport) in listOf("portrait" to DpSize(680.dp, 910.dp), "landscape" to DpSize(1000.dp, 710.dp))) {
            compose.runOnIdle { size.value = viewport }
            compose.waitForIdle()
            val image = capture()
            File(dir, "synthetic-item-removal-$layout-$name.png").outputStream().use {
                check(image.compress(Bitmap.CompressFormat.PNG, 100, it))
            }
        }
    }

    private fun install(app: WorkbenchComponents) = mutableStateOf(DpSize(1000.dp, 710.dp)).also { size ->
        compose.setContent {
            DeviceConfigurationOverride(DeviceConfigurationOverride.ForcedSize(size.value)) { PatrolApp(app.model) }
        }
    }

    private fun login(app: WorkbenchComponents) {
        compose.waitUntil(20_000) { app.model.ui.value.online && !app.model.ui.value.busy }
        compose.runOnIdle { app.model.login("fixture-inspector", "test-only") }
        waitHome(app)
    }

    private fun waitHome(app: WorkbenchComponents) =
        compose.waitUntil(20_000) { app.model.ui.value.screen is Screen.Home && !app.model.ui.value.busy }

    private fun openDetail(fixture: WorkbenchDeviceFixture, app: WorkbenchComponents, line: ProductionLine = LINE) {
        waitHome(app)
        compose.runOnIdle { app.model.openPatrol() }
        openFromLines(fixture, app, line)
    }

    private fun openFromLines(fixture: WorkbenchDeviceFixture, app: WorkbenchComponents, line: ProductionLine) {
        compose.waitUntil(20_000) { (app.model.ui.value.screen as? Screen.Lines)?.load is Load.Data }
        compose.runOnIdle { app.model.select(line) }
        compose.waitUntil(20_000) { (app.model.ui.value.screen as? Screen.Tasks)?.load is Load.Data }
        compose.runOnIdle { app.model.openTask(fixture.task(line)) }
        compose.waitUntil(20_000) { (app.model.ui.value.screen as? Screen.Detail)?.load is Load.Data }
    }

    private fun openWorkbench(fixture: WorkbenchDeviceFixture, app: WorkbenchComponents) {
        openDetail(fixture, app)
        enterWorkbench(app)
    }

    private fun enterWorkbench(app: WorkbenchComponents) {
        button("Open sample workbench").performClick()
        compose.waitUntil(20_000) {
            val state = app.model.workbench?.ui?.value
            state?.target != null && !state.busy
        }
    }

    private fun activate(app: WorkbenchComponents) {
        button("Activate local capture").performClick()
        compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.canEdit == true }
    }

    private fun offline(app: WorkbenchComponents) {
        app.connectivity.value = false
        compose.waitUntil(20_000) {
            app.model.ui.value.screen is Screen.SampleWorkbench && app.model.ui.value.offline != null && !app.model.ui.value.busy
        }
    }

    private fun choose(fixture: WorkbenchDeviceFixture, index: Int) {
        compose.onNodeWithTag("inspection-item-" + fixture.definitions[index].id).performClick()
    }

    private fun chooseQualitative(judgment: String) {
        pane().performScrollToNode(hasText(judgment) and hasClickAction())
        button(judgment).performClick()
    }

    private fun saveSample() {
        pane().performScrollToNode(hasText("Save locally") and hasClickAction())
        button("Save locally").performClick()
    }

    private fun awaitCount(app: WorkbenchComponents, count: Int) =
        compose.waitUntil(20_000) {
            val state = app.model.workbench?.ui?.value
            state != null && state.draft?.history?.size == count && !state.busy && !state.retryPending
        }

    private fun awaitRetry(app: WorkbenchComponents) =
        compose.waitUntil(20_000) { app.model.workbench?.ui?.value?.retryPending == true }

    private fun draft(app: WorkbenchComponents) = requireNotNull(app.model.workbench?.ui?.value?.draft)

    private fun button(label: String) = compose.onNode(hasText(label) and hasClickAction())

    private fun pane() = compose.onNodeWithTag("workbench-items")

    private fun close(app: WorkbenchComponents) = compose.runOnIdle { app.model.leaveForeground() }

    private fun advance(fixture: WorkbenchDeviceFixture, millis: Long) {
        val now = fixture.device.sample
        fixture.device.sample = now.copy(elapsedMs = now.elapsedMs + millis, utcMs = now.utcMs + millis)
    }

    private fun capture(name: String) {
        if (InstrumentationRegistry.getArguments().getString("ipqcWorkbenchScreenshots") != "true") return
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val dir = File(context.cacheDir, "workbench-evidence").apply { mkdirs() }
        File(dir, "workbench-" + name + ".jpg").outputStream().use {
            compose.onNodeWithTag("sample-workbench").captureToImage().asAndroidBitmap().compress(Bitmap.CompressFormat.JPEG, 88, it)
        }
    }

    private enum class Fault { None, Before, After, Hold }

    private class FaultRows(private val base: CacheRows) : CacheRows by base {
        val mode = AtomicReference(Fault.None)
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        private val writes = AtomicInteger()

        override suspend fun replace(slot: String, sealed: ByteArray) {
            base.replace(slot, sealed)
            writes.incrementAndGet()
            if (mode.compareAndSet(Fault.Before, Fault.None)) error("Synthetic rollback")
            if (mode.compareAndSet(Fault.Hold, Fault.None)) {
                entered.complete(Unit)
                release.await()
            }
        }

        override suspend fun <T> transaction(block: suspend () -> T): T {
            val before = writes.get()
            val result = base.transaction(block)
            if (writes.get() > before && mode.compareAndSet(Fault.After, Fault.None)) error("Synthetic lost acknowledgement")
            return result
        }
    }
}
