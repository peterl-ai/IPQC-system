@file:OptIn(ExperimentalLayoutApi::class)

package com.jaxpower.ipqc.patrol.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDefinition
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.ProductionLine
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
internal fun PatrolPage(ui: PatrolUi, model: PatrolViewModel) {
    when (val screen = ui.screen) {
        Screen.Login -> CredentialsPage(ui, change = false, submit = model::login, resume = model::resume, forget = model::logout)
        is Screen.ChangePassword -> CredentialsPage(ui, change = true, submit = model::changePassword, resume = {}, forget = {})
        is Screen.Home -> HomePage(screen.operator.displayName, model::openPatrol)
        is Screen.Lines -> LinesPage(screen.load, ui.offline != null, model::select, model::refresh)
        is Screen.Tasks -> TasksPage(screen, ui.busy, model::refresh, model::switchLine,
            model::previousPage, model::nextPage, model::openTask, cached = ui.offline != null)
        is Screen.Detail -> DetailPage(screen, ui.busy, model::refresh, model::switchLine, cached = ui.offline != null)
    }
}

@Composable
internal fun CredentialsPage(ui: PatrolUi, change: Boolean, submit: (String, String) -> Unit, resume: () -> Unit, forget: () -> Unit) {
    var first by remember(change) { mutableStateOf("") }
    var second by remember(change) { mutableStateOf("") }
    val canSend = ui.online && ui.configured && !ui.busy
    Box(Modifier.fillMaxSize().imePadding(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 520.dp).fillMaxWidth().verticalScroll(rememberScrollState()).padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(if (change) "Change password" else "Personal sign-in", style = MaterialTheme.typography.headlineMedium)
            Text(if (change) "Change your password to continue. Sign in again after saving."
                else "Connect to the plant network and sign in with your IPQA account.")
            ui.issue?.let { Notice(it.message, error = true) }
            ui.message?.let { Notice(it) }
            OutlinedTextField(
                value = first, onValueChange = { first = it }, enabled = canSend,
                label = { Text(if (change) "Current password" else "Username") }, singleLine = true,
                visualTransformation = if (change) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
                keyboardOptions = KeyboardOptions(keyboardType = if (change) KeyboardType.Password else KeyboardType.Text),
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = second, onValueChange = { second = it }, enabled = canSend,
                label = { Text(if (change) "New password" else "Password") }, singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = {
                    val usernameOrCurrent = first
                    val password = second
                    if (change) first = ""
                    second = ""
                    submit(usernameOrCurrent, password)
                },
                enabled = canSend && first.isNotBlank() && second.isNotEmpty(),
                modifier = Modifier.fillMaxWidth().defaultMinSize(minHeight = 52.dp),
            ) { Text(if (change) "Save new password" else "Sign in") }
            if (ui.busy) Loading(if (ui.online) "Contacting IPQC" else "Checking saved access")
            if (!change) {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = resume, enabled = canSend) { Text("Resume saved session") }
                    TextButton(onClick = forget, enabled = ui.configured && !ui.busy) { Text("Clear saved sign-in") }
                }
            }
        }
    }
}

@Composable
internal fun HomePage(name: String, openPatrol: () -> Unit) = PageScroll {
    Text("Home", style = MaterialTheme.typography.headlineMedium)
    Text(name, style = MaterialTheme.typography.titleMedium)
    AdaptivePair(
        main = {
            Card(onClick = openPatrol, modifier = Modifier.fillMaxWidth().defaultMinSize(minHeight = 100.dp)) {
                Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Patrol", style = MaterialTheme.typography.titleLarge)
                    Text("Choose a production line and view its patrol tasks.")
                }
            }
        },
        secondary = {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Material Verification", style = MaterialTheme.typography.titleLarge)
                    Text("Not available")
                }
            }
        },
    )
}

@Composable
private fun LinesPage(
    load: Load<List<ProductionLine>>, cached: Boolean, select: (ProductionLine) -> Unit, refresh: () -> Unit,
) = PageScroll {
    Text("Select a production line", style = MaterialTheme.typography.headlineMedium)
    when (load) {
        Load.Loading -> Loading("Loading production lines")
        is Load.Failed -> Failure(load.issue, refresh)
        is Load.Data -> {
            if (load.value.isEmpty()) Notice("No production lines downloaded. Open Patrol online first.")
            else AdaptivePair(
                main = { LineCard(load.value[0], cached, select) },
                secondary = { LineCard(load.value[1], cached, select) },
            )
        }
    }
}

@Composable
private fun LineCard(line: ProductionLine, cached: Boolean, select: (ProductionLine) -> Unit) {
    Card(onClick = { select(line) }, modifier = Modifier.fillMaxWidth().defaultMinSize(minHeight = 104.dp)) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(line.name, style = MaterialTheme.typography.titleLarge)
            Text(line.code, style = MaterialTheme.typography.bodyMedium)
            if (cached) Text(line.id, style = MaterialTheme.typography.labelMedium)
        }
    }
}

@Composable
internal fun TasksPage(
    screen: Screen.Tasks, busy: Boolean, refresh: () -> Unit, switchLine: () -> Unit,
    previous: () -> Unit, next: () -> Unit, select: (LineTaskSummary) -> Unit,
    cached: Boolean = false,
) {
    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { TaskHeading("Patrol tasks", screen.line, busy, refresh, switchLine, cached) }
        when (val load = screen.load) {
            Load.Loading -> item { Loading("Loading patrol tasks") }
            is Load.Failed -> item { Failure(load.issue, refresh) }
            is Load.Data -> {
                if (load.value.items.isEmpty()) item {
                    Notice(if (cached) "No downloaded tasks on this page for this line." else "No patrol tasks on this page.")
                }
                items(load.value.items, key = { it.id }) { task ->
                    Card(onClick = { select(task) }, modifier = Modifier.fillMaxWidth().defaultMinSize(minHeight = 80.dp)) {
                        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text(task.taskNo, style = MaterialTheme.typography.titleLarge)
                            Text(if (cached) "${task.status} · Cached" else task.status)
                            if (cached) Text("Last validated ${task.validatedAccess.validatedAtUtc}")
                            Text(plantTime(task.scheduledOccurrenceUtc))
                            task.standardNameSnapshot?.let { Text(it) }
                            Text(task.readFoundationStatus, style = MaterialTheme.typography.labelMedium)
                        }
                    }
                }
                item {
                    FlowRow(
                        Modifier.fillMaxWidth().padding(bottom = 16.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        OutlinedButton(onClick = previous, enabled = !busy && screen.page > 1) { Text("Previous") }
                        OutlinedButton(onClick = next, enabled = !busy && load.value.hasNext) { Text("Next") }
                        val label = if (cached) "Cached page ${screen.page}" else "Page ${screen.page} · ${load.value.total} total"
                        Text(label, Modifier.padding(top = 14.dp))
                    }
                }
            }
        }
    }
}

@Composable
internal fun DetailPage(
    screen: Screen.Detail, busy: Boolean, refresh: () -> Unit, switchLine: () -> Unit, cached: Boolean = false,
) {
    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { TaskHeading("Task ${screen.task.taskNo}", screen.line, busy, refresh, switchLine, cached) }
        when (val load = screen.load) {
            Load.Loading -> item { Loading("Loading task details") }
            is Load.Failed -> item { Failure(load.issue, refresh) }
            is Load.Data -> {
                item {
                    if (cached) Text("Last validated ${load.value.summary.validatedAccess.validatedAtUtc}")
                    DetailSummary(load.value)
                }
                item { Text("Frozen inspection items", style = MaterialTheme.typography.titleLarge) }
                items(load.value.items, key = { it.id }) { Definition(it) }
                item { Box(Modifier.padding(bottom = 16.dp)) }
            }
        }
    }
}

@Composable
private fun DetailSummary(detail: LineTaskDetail) {
    val task = detail.summary
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(9.dp)) {
            Text("Read only", style = MaterialTheme.typography.titleMedium)
            AdaptivePair(
                main = {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Fact("Status", task.status)
                        Fact("Server line", task.lineNameSnapshot)
                        Fact("Scheduled", plantTime(task.scheduledOccurrenceUtc))
                    }
                },
                secondary = {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Fact("Plan", listOfNotNull(task.planNoSnapshot, task.planNameSnapshot).joinToString(" · "))
                        Fact("Standard", task.standardNameSnapshot)
                        Fact("Readiness", task.readFoundationStatus)
                        Fact("Planning snapshot", task.planningSnapshotStatus)
                    }
                },
            )
        }
    }
}

@Composable
internal fun Definition(item: LineTaskDefinition) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(item.inspectionItem, style = MaterialTheme.typography.titleLarge)
            Fact("Process", listOf(item.processCode, item.processName).filter { it.isNotBlank() }.joinToString(" · "))
            Fact("Inspection content", item.inspectionContent)
            HorizontalDivider()
            AdaptivePair(
                main = {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Fact("Inspection type", item.inspectionType)
                        Fact("Sample count", item.sampleCount)
                        Fact("Sampling plan", item.samplingPlan)
                    }
                },
                secondary = {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        if (item.lowerLimitValue.isNotBlank()) Fact("Lower limit", "${item.lowerLimitOperator} ${item.lowerLimitValue}")
                        if (item.upperLimitValue.isNotBlank()) Fact("Upper limit", "${item.upperLimitOperator} ${item.upperLimitValue}")
                        Fact("Photo requirement (source text)", item.photoRequirement)
                    }
                },
            )
        }
    }
}

@Composable
private fun TaskHeading(
    title: String, line: ProductionLine, busy: Boolean, refresh: () -> Unit, switchLine: () -> Unit, cached: Boolean,
) {
    Column(Modifier.padding(top = 20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, style = MaterialTheme.typography.headlineMedium)
        Text("${line.name} · ${line.code}", style = MaterialTheme.typography.titleMedium)
        if (cached) Text(line.id, style = MaterialTheme.typography.labelMedium)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = refresh, enabled = !busy) { Text(if (cached) "Reload cache" else "Refresh") }
            TextButton(onClick = switchLine, enabled = !busy) { Text("Switch line") }
        }
    }
}

@Composable
private fun Fact(label: String, value: String?) {
    Column {
        Text(label, style = MaterialTheme.typography.labelMedium)
        Text(value?.takeIf(String::isNotBlank) ?: "Not recorded", style = MaterialTheme.typography.bodyLarge)
    }
}

@Composable
private fun PageScroll(content: @Composable ColumnScope.() -> Unit) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 1080.dp).fillMaxWidth().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
            content = content,
        )
    }
}

@Composable
private fun AdaptivePair(main: @Composable () -> Unit, secondary: @Composable () -> Unit) {
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (maxWidth >= 680.dp) {
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                Box(Modifier.weight(1f)) { main() }
                Box(Modifier.weight(1f)) { secondary() }
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                main()
                secondary()
            }
        }
    }
}

@Composable
private fun Failure(issue: Issue, retry: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Notice(issue.message, error = true)
        OutlinedButton(onClick = retry) { Text("Try again") }
    }
}

@Composable
private fun Notice(text: String, error: Boolean = false) {
    Surface(
        color = if (error) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer,
        contentColor = if (error) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onPrimaryContainer,
        shape = MaterialTheme.shapes.medium,
        modifier = Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite },
    ) { Text(text, Modifier.padding(14.dp)) }
}

@Composable
private fun Loading(label: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier.padding(12.dp).semantics { liveRegion = LiveRegionMode.Polite }) {
        CircularProgressIndicator()
        Text(label)
    }
}

private val plantDate = DateTimeFormatter.ofPattern("MMM d, yyyy · h:mm a z", Locale.ENGLISH)
    .withZone(ZoneId.of("America/New_York"))
private fun plantTime(value: String) = plantDate.format(Instant.parse(value))
