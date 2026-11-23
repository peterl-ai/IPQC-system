package com.jaxpower.ipqc.patrol

import android.graphics.Bitmap
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
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.SECOND_LINE
import com.jaxpower.ipqc.patrol.api.ProductionLine
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
