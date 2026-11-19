package com.jaxpower.ipqc.patrol

import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.api.CurrentUser
import com.jaxpower.ipqc.patrol.api.NativeAuth
import com.jaxpower.ipqc.patrol.api.NativeLogin
import com.jaxpower.ipqc.patrol.api.NativeRefresh
import com.jaxpower.ipqc.patrol.api.PasswordChange
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.auth.CredentialVault
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.auth.StoredRefresh
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.ui.PatrolApp
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.ui.Screen
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.Response

@RunWith(AndroidJUnit4::class)
class PatrolRecreationTest {
    @Test fun activityRecreationRetainsOneInFlightLoginWithoutOtherWrites() {
        val api = PendingLoginApi()
        val vault = object : CredentialVault {
            override fun read(): StoredRefresh? = null
            override fun replace(record: StoredRefresh) = error("A forced session must never persist")
            override fun clear() = Unit
        }
        val clock = Clock.fixed(Instant.parse("2026-11-19T14:00:00Z"), ZoneOffset.UTC)
        val sessions = SessionRepository(api, vault, "https://recreate.example.invalid/", clock)
        val factory = viewModelFactory {
            initializer { PatrolViewModel(sessions, PatrolRepository(api, sessions), flowOf(true)) }
        }

        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var original: PatrolViewModel
            scenario.onActivity { activity ->
                original = ViewModelProvider(activity, factory)[PatrolViewModel::class.java]
                activity.setContent { PatrolApp(original) }
            }
            runBlocking { withTimeout(5_000) { original.ui.first { it.online && !it.busy } } }

            scenario.onActivity { original.login("fixture", "test-only") }
            runBlocking { withTimeout(5_000) { api.started.await() } }

            scenario.recreate()
            scenario.onActivity { activity ->
                val retained = ViewModelProvider(activity, factory)[PatrolViewModel::class.java]
                assertSame(original, retained)
                activity.setContent { PatrolApp(retained) }
                retained.login("fixture", "test-only")
            }
            api.finish.complete(Unit)
            runBlocking { withTimeout(5_000) { original.ui.first { it.screen is Screen.ChangePassword } } }
            assertEquals(1, api.calls.get())
        }
    }

    private class PendingLoginApi : PatrolApi {
        val calls = AtomicInteger()
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()

        override suspend fun login(input: NativeLogin): Response<NativeAuth> {
            calls.incrementAndGet()
            started.complete(Unit)
            finish.await()
            return Response.success(NativeAuth(
                "fixture-access", "2026-11-19T14:05:00Z",
                CurrentUser("10000000-0000-0000-0000-000000000001", "fixture", "Fixture inspector",
                    "IPQA", true, true, emptyList()),
                "fixture-refresh", "20000000-0000-0000-0000-000000000001", "fixture",
                "2026-11-26T14:00:00Z",
            ))
        }

        override suspend fun refresh(input: NativeRefresh) = error("Unexpected refresh")
        override suspend fun logout(bearer: String, input: NativeRefresh) = error("Unexpected logout")
        override suspend fun changePassword(bearer: String, input: PasswordChange) = error("Unexpected password change")
        override suspend fun verify(bearer: String) = error("Forced session must not verify patrol access")
        override suspend fun lines(bearer: String) = error("Forced session must not list lines")
        override suspend fun tasks(bearer: String, lineId: String, page: Int, pageSize: Int) = error("Unexpected task read")
        override suspend fun detail(bearer: String, taskId: String, lineId: String) = error("Unexpected detail read")
    }
}
