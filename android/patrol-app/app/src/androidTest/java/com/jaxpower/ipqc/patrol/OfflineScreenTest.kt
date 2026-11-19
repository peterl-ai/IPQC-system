package com.jaxpower.ipqc.patrol

import android.content.pm.ApplicationInfo
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.DeviceConfigurationOverride
import androidx.compose.ui.test.ForcedSize
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.TASK
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.ui.PatrolApp
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.ui.Screen
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflineScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test fun offlineShowsActualLineAndFrozenDetailWithoutExecutionControlsInEitherOrientation() {
        OfflineDeviceFixture().use { device ->
            runBlocking { device.download() }
            val model = PatrolViewModel(device.sessions, device.patrol, flowOf(false), device.openCache())
            val size = mutableStateOf(DpSize(600.dp, 900.dp))
            compose.setContent {
                DeviceConfigurationOverride(DeviceConfigurationOverride.ForcedSize(size.value)) { PatrolApp(model) }
            }
            try {
                compose.waitUntil(8_000) { model.ui.value.screen is Screen.Home && !model.ui.value.busy }
                compose.onNodeWithText("Offline · Cached data · Read only").assertIsDisplayed()
                compose.onNodeWithText("Offline access expires 2026-11-22T14:00:00Z").assertIsDisplayed()
                compose.onNodeWithText("Patrol").performClick()
                compose.waitUntil(8_000) { model.ui.value.screen is Screen.Lines && !model.ui.value.busy }
                compose.onNodeWithText(LINE.id).assertIsDisplayed()
                compose.onNodeWithText(LINE.name).performClick()
                compose.waitUntil(8_000) { model.ui.value.screen is Screen.Tasks && !model.ui.value.busy }
                compose.onNodeWithText(TASK.taskNo).performClick()
                compose.waitUntil(8_000) { model.ui.value.screen is Screen.Detail && !model.ui.value.busy }
                compose.onNodeWithText("Last validated $NOW").assertIsDisplayed()
                for (sizeOverride in listOf(DpSize(600.dp, 900.dp), DpSize(1000.dp, 600.dp))) {
                    compose.runOnIdle { size.value = sizeOverride }
                    compose.onNodeWithText("Offline · Cached data · Read only").assertIsDisplayed()
                    for (action in listOf("Add Sample", "Remove", "Save Draft", "Submit", "Formal Submit",
                        "Capture Photo", "Approve", "Reject", "Assign PIC")) {
                        compose.onNodeWithText(action, ignoreCase = true).assertDoesNotExist()
                    }
                }
                assertEquals(5, device.api.calls.get())
            } finally {
                compose.runOnIdle { model.leaveForeground() }
            }
        }
    }

    @Test fun activityRecreationKeepsValidAdmissionAndARecreatedProcessRejectsExpiry() {
        OfflineDeviceFixture().use { device ->
            runBlocking { device.download() }
            fun factory(cache: OfflineCache) = viewModelFactory {
                initializer { PatrolViewModel(device.sessions, device.patrol, flowOf(false), cache) }
            }
            val firstCache = device.openCache()
            val firstFactory = factory(firstCache)
            ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
                lateinit var original: PatrolViewModel
                scenario.onActivity { activity ->
                    original = ViewModelProvider(activity, firstFactory)[PatrolViewModel::class.java]
                    activity.setContent { PatrolApp(original) }
                }
                runBlocking {
                    withTimeoutOrNull(15_000) { original.ui.first { it.screen is Screen.Home && it.offline != null } }
                        ?: error("Expected the original Activity to admit the verified session. Issue ${original.ui.value.issue}")
                }
                scenario.recreate()
                scenario.onActivity { activity ->
                    val retained = ViewModelProvider(activity, firstFactory)[PatrolViewModel::class.java]
                    assertSame(original, retained)
                    assertNotNull(retained.ui.value.offline)
                    activity.setContent { PatrolApp(retained) }
                    retained.leaveForeground()
                }
            }
            // Drain the old cache instance before injecting reboot evidence for the new one.
            runBlocking { assertNotNull(firstCache.resume()) }
            device.sample = device.sample.copy(boot = device.sample.boot + 1, elapsedMs = 900,
                utcMs = device.sample.utcMs + 72 * 3600 * 1_000L)
            val restartedFactory = factory(device.openCache())
            ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
                lateinit var recreated: PatrolViewModel
                scenario.onActivity { activity ->
                    recreated = ViewModelProvider(activity, restartedFactory)[PatrolViewModel::class.java]
                    activity.setContent { PatrolApp(recreated) }
                }
                val locked = runBlocking {
                    withTimeoutOrNull(15_000) { recreated.ui.first { it.issue == Issue.OFFLINE_EXPIRED } }
                        ?: error("Expected expiry after reopening app state. Issue ${recreated.ui.value.issue}")
                }
                assertEquals(Screen.Login, locked.screen)
                assertNull(locked.offline)
                assertEquals(5, device.api.calls.get())
            }
        }
    }

    @Test fun productionActivityIsSecureAndApplicationDisablesBackup() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                assertTrue(activity.window.attributes.flags and WindowManager.LayoutParams.FLAG_SECURE != 0)
                assertEquals(0, activity.applicationInfo.flags and ApplicationInfo.FLAG_ALLOW_BACKUP)
            }
        }
    }
}
