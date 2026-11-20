package com.jaxpower.ipqc.patrol.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import java.time.Duration

private val PatrolColors = lightColorScheme(
    primary = Color(0xFF075D61),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFD1EEEB),
    onPrimaryContainer = Color(0xFF063737),
    background = Color(0xFFF4F7F8),
    surface = Color.White,
    onBackground = Color(0xFF14232D),
    onSurface = Color(0xFF14232D),
    outlineVariant = Color(0xFFD4DEE3),
)

private enum class Route { Login, Password, Home, Lines, Tasks, Detail }

private fun Screen.route(): Route = when (this) {
    Screen.Login -> Route.Login
    is Screen.ChangePassword -> Route.Password
    is Screen.Home -> Route.Home
    is Screen.Lines -> Route.Lines
    is Screen.Tasks -> Route.Tasks
    is Screen.Detail -> Route.Detail
}

@Composable
fun PatrolApp(viewModel: PatrolViewModel) {
    val ui by viewModel.ui.collectAsStateWithLifecycle()
    val navigation = rememberNavController()
    val route = ui.screen.route()

    MaterialTheme(colorScheme = PatrolColors) {
        Surface(color = MaterialTheme.colorScheme.background, modifier = Modifier.fillMaxSize()) {
            Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
                PatrolBar(ui, viewModel)
                ui.offline?.let { access ->
                    val ageMinutes = Duration.between(access.verifiedAt, access.apparentNow).toMinutes()
                    val age = if (ageMinutes < 60) "${ageMinutes}m ago" else "${ageMinutes / 60}h ${ageMinutes % 60}m ago"
                    Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp)) {
                            Text("Offline · Cached data · Read only", style = MaterialTheme.typography.titleSmall)
                            Text("Last online verification ${access.verifiedAt} ($age)")
                            Text("Offline access expires ${access.expiresAt}")
                            if (ui.online) TextButton(onClick = viewModel::reconnect, enabled = !ui.busy) {
                                Text("Retry online verification")
                            }
                        }
                    }
                }
                LaunchedEffect(route) {
                    if (navigation.currentDestination?.route != route.name) {
                        navigation.navigate(route.name) {
                            // A server identity lives in the ViewModel, never in a back-stack argument.
                            popUpTo("patrol-root")
                            launchSingleTop = true
                        }
                    }
                }
                NavHost(navigation, startDestination = Route.Login.name, route = "patrol-root",
                    modifier = Modifier.weight(1f)) {
                    for (destination in Route.entries) {
                        composable(destination.name) {
                            // Never let a restored/stale route render an earlier session or line.
                            if (ui.screen.route() == destination) {
                                PatrolPage(ui, viewModel)
                            }
                        }
                    }
                }
                BackHandler(route in setOf(Route.Lines, Route.Tasks, Route.Detail)) { viewModel.back() }
            }
        }
    }
}

@Composable
private fun PatrolBar(ui: PatrolUi, viewModel: PatrolViewModel) {
    val screen = ui.screen
    Surface(color = MaterialTheme.colorScheme.surface, tonalElevation = 1.dp) {
        Row(
            Modifier.fillMaxWidth().defaultMinSize(minHeight = 64.dp).padding(horizontal = 16.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            if (screen is Screen.Lines || screen is Screen.Tasks || screen is Screen.Detail) {
                TextButton(onClick = viewModel::back, enabled = !ui.busy) { Text("Back") }
            }
            Text("IPQC Patrol", style = MaterialTheme.typography.titleLarge, modifier = Modifier.widthIn(max = 260.dp))
            Spacer(Modifier.weight(1f))
            if (screen != Screen.Login) {
                TextButton(onClick = viewModel::logout) { Text("Sign out") }
            }
        }
    }
}
