package com.jaxpower.ipqc.patrol

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.jaxpower.ipqc.patrol.api.createPatrolApi
import com.jaxpower.ipqc.patrol.api.httpsOrigin
import com.jaxpower.ipqc.patrol.auth.KeystoreCredentialVault
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.ui.PatrolApp
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.callbackFlow

class PatrolApplication : Application() {
    val patrolViewModelFactory: ViewModelProvider.Factory by lazy {
        val origin = httpsOrigin(BuildConfig.SERVER_URL)
        val api = origin?.let { createPatrolApi(it) }
        val sessions = api?.let {
            SessionRepository(it, KeystoreCredentialVault(this), requireNotNull(origin).toString())
        }
        val patrol = api?.let { PatrolRepository(it, requireNotNull(sessions)) }
        viewModelFactory { initializer { PatrolViewModel(sessions, patrol, networkAvailability(this@PatrolApplication)) } }
    }
}

class MainActivity : ComponentActivity() {
    private val viewModel by viewModels<PatrolViewModel> { (application as PatrolApplication).patrolViewModelFactory }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { PatrolApp(viewModel) }
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
