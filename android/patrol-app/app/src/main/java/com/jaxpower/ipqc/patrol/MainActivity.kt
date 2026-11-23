package com.jaxpower.ipqc.patrol

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.api.httpsOrigin
import com.jaxpower.ipqc.patrol.api.patrolRetrofit
import com.jaxpower.ipqc.patrol.auth.KeystoreCredentialVault
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.ui.PatrolApp
import com.jaxpower.ipqc.patrol.ui.Screen
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.work.OnlineWorkCoordinator
import com.jaxpower.ipqc.patrol.work.ProtectedWorkRows
import com.jaxpower.ipqc.patrol.work.SharedWorkStore
import com.jaxpower.ipqc.patrol.work.WorkContextApi
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.callbackFlow
import okhttp3.HttpUrl

class PatrolApplication : Application() {
    private val connection by lazy { httpsOrigin(BuildConfig.SERVER_URL)?.let { PatrolConnection(this, it) } }

    internal val onlineWorkCoordinator get() = connection?.onlineWork

    val patrolViewModelFactory: ViewModelProvider.Factory by lazy {
        val configured = connection
        viewModelFactory {
            initializer {
                PatrolViewModel(configured?.sessions, configured?.patrol,
                    networkAvailability(this@PatrolApplication), configured?.offline,
                    if (BuildConfig.DEBUG && BuildConfig.IPQC_ENABLE_SAMPLE_WORKBENCH) configured?.onlineWork else null)
            }
        }
    }
}

private class PatrolConnection(context: Context, url: HttpUrl) {
    private val origin = url.toString()
    private val retrofit = patrolRetrofit(url)
    private val api = retrofit.create(PatrolApi::class.java)
    private val database = TaskCacheDatabase.openWorkspace(context)
    val offline = OfflineCache.open(context, origin, database)
    val sessions = SessionRepository(api, KeystoreCredentialVault(context), origin, offline = offline)
    val patrol = PatrolRepository(api, sessions, offline)
    val onlineWork by lazy {
        val store = SharedWorkStore(ProtectedWorkRows(RoomCacheRows(database), origin), sessions)
        OnlineWorkCoordinator(origin, patrol, retrofit.create(WorkContextApi::class.java), sessions, store)
    }
}

class MainActivity : ComponentActivity() {
    private val viewModel by viewModels<PatrolViewModel> { (application as PatrolApplication).patrolViewModelFactory }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        enableEdgeToEdge()
        setContent { PatrolApp(viewModel) }
    }

    override fun onStart() {
        super.onStart()
        viewModel.returnToForeground()
    }

    override fun onStop() {
        if (!isChangingConfigurations || viewModel.ui.value.screen !is Screen.SampleWorkbench) {
            viewModel.leaveForeground()
        }
        super.onStop()
    }
}

private fun networkAvailability(context: Context) = callbackFlow {
    val manager = context.getSystemService(ConnectivityManager::class.java)
    if (manager == null) {
        trySend(false)
        close()
        return@callbackFlow
    }
    val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) { trySend(true) }
        override fun onLost(network: Network) { trySend(manager.activeNetwork != null) }
    }
    try {
        // Do not require public-Internet validation for a plant-local HTTPS server.
        manager.registerDefaultNetworkCallback(callback)
    } catch (_: Exception) {
        trySend(false)
        close()
        return@callbackFlow
    }
    trySend(manager.activeNetwork != null)
    awaitClose { manager.unregisterNetworkCallback(callback) }
}
