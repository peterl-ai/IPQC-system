package com.jaxpower.ipqc.patrol.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.jaxpower.ipqc.patrol.api.LineTaskDefinition
import com.jaxpower.ipqc.patrol.work.FrozenSampleType
import com.jaxpower.ipqc.patrol.work.ItemDisposition
import com.jaxpower.ipqc.patrol.work.WorkProblem
import com.jaxpower.ipqc.patrol.work.LocalSampleHistory
import com.jaxpower.ipqc.patrol.work.LocalSampleProgress
import com.jaxpower.ipqc.patrol.work.LocalSampleValue
import com.jaxpower.ipqc.patrol.work.MAX_CURRENT_SAMPLES
import com.jaxpower.ipqc.patrol.work.QualitativeJudgment
import com.jaxpower.ipqc.patrol.work.SampleInput
import com.jaxpower.ipqc.patrol.work.SampleKind
import com.jaxpower.ipqc.patrol.work.SamplePayload
import com.jaxpower.ipqc.patrol.work.WorkAttempt
import kotlinx.coroutines.launch

private data class SampleForm(
    val previous: LocalSampleValue? = null,
    val judgment: QualitativeJudgment? = null,
    val decimal: String = "",
    val barcode: String = "",
) {
    fun input(type: FrozenSampleType) = when (type) {
        FrozenSampleType.Qualitative -> judgment?.let { SampleInput.Qualitative(it) }
        FrozenSampleType.Quantitative -> decimal.takeIf(String::isNotEmpty)?.let {
            SampleInput.Quantitative(it, barcode.ifEmpty { null })
        }
    }
}

@Composable
internal fun SampleWorkbenchPage(patrol: PatrolUi, model: PatrolViewModel, pilot: SampleWorkbenchModel) {
    val state by pilot.ui.collectAsStateWithLifecycle()
    val target = state.target ?: return
    val origin = (patrol.screen as? Screen.SampleWorkbench)?.origin ?: return
    val definitions = remember(target) { target.detail.items.sortedBy { it.sequenceNo } }
    val selectedId = state.selectedItemId
    var form by remember(target, selectedId) { mutableStateOf(SampleForm()) }
    var removal by remember(target, selectedId) { mutableStateOf<LocalSampleValue?>(null) }
    var itemRemoval by remember(target, selectedId) { mutableStateOf<WorkAttempt?>(null) }
    val focus = LocalFocusManager.current
    val typing = WindowInsets.ime.getBottom(LocalDensity.current) > 0
    LaunchedEffect(state.lastSavedId) {
        if (state.lastSavedId != null) {
            form = SampleForm()
            removal = null
        }
    }
    LaunchedEffect(state.draft, state.hasLocalAccess) {
        if (state.draft == null || !state.hasLocalAccess) {
            form = SampleForm()
            removal = null
            itemRemoval = null
        }
    }

    Column(Modifier.fillMaxSize().imePadding().testTag("sample-workbench").padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (patrol.busy) {
            Text("Checking protected local access", Modifier.padding(16.dp))
            return@Column
        }
        val canUseOnline = patrol.online && patrol.offline == null
        if (typing) {
            Text("Development Pilot / Local Draft · " + origin.task.taskNo, style = MaterialTheme.typography.labelLarge)
        } else {
            WorkbenchHeading(origin, state, canUseOnline, arm = { model.armWorkbench(target) },
                reload = { model.reloadWorkbench(target) }, switchLine = model::switchLine)
        }
        state.error?.let { WorkbenchNotice(it, error = true) }
        state.saved?.let { WorkbenchNotice(it) }
        if (state.uploadingRemoval) WorkbenchNotice("Uploading removal…")
        if (state.removalSavePending) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { model.checkWorkbenchRemovalSave(target) }, enabled = !state.busy) {
                    Text("Check removal save")
                }
                Button(onClick = { model.retryWorkbenchRemovalSave(target) }, enabled = !state.busy) {
                    Text("Retry same removal save")
                }
            }
        }
        if (state.retryPending) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { pilot.checkSave(target) }, enabled = !state.busy) { Text("Check local save") }
                Button(onClick = { pilot.retry(target) }, enabled = !state.busy) { Text("Retry same save") }
            }
        }

        val draft = state.draft
        val progress = draft?.progress.orEmpty().associateBy { it.attempt.taskItemId }
        val history = draft?.history.orEmpty().groupBy { it.operation.envelope.operation.attempt.taskItemId }
        val selected = definitions.singleOrNull { it.id == selectedId } ?: return@Column
        val removalView = state.removalView(canUseOnline)
        fun select(item: LineTaskDefinition) {
            if (selectedId != item.id) {
                focus.clearFocus()
                pilot.selectItem(target, item.id)
            }
        }

        val detailPane: @Composable (Modifier) -> Unit = { modifier ->
            val requirement = progress[selected.id]
            WorkbenchItem(modifier, selected, requirement, state, form, change = { form = it },
                save = { attempt, input ->
                    focus.clearFocus()
                    pilot.save(target, attempt, input, form.previous)
                },
                correct = { sample ->
                    form = SampleForm(sample, QualitativeJudgment.entries.firstOrNull { it.name == sample.value?.judgment },
                        sample.value?.inspectionValue.orEmpty(), sample.value?.barcode.orEmpty())
                },
                remove = { removal = it },
                itemRemovalPanel = {
                    ItemRemovalPanel(selected.id, removalView, canUseOnline, state.blocksNavigation,
                        state.blocked == WorkProblem.StaleGrant,
                        canStart = requirement?.attempt?.let(state::canStartRemoval) == true,
                        start = {
                            focus.clearFocus()
                            itemRemoval = requirement?.attempt
                        },
                        upload = { model.uploadWorkbenchItemRemoval(target, it) },
                        checkReceipt = { model.checkWorkbenchItemRemovalReceipt(target, it) })
                })
        }

        BoxWithConstraints(Modifier.weight(1f).fillMaxWidth()) {
            if (maxWidth >= 760.dp) {
                Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    LazyColumn(Modifier.weight(0.34f).fillMaxHeight(), contentPadding = PaddingValues(bottom = 20.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        item { Text("Inspection items", style = MaterialTheme.typography.titleLarge) }
                        items(definitions, key = { it.id }) { item ->
                            ItemChoice(item, item.id == selectedId, progress[item.id], history[item.id].orEmpty(),
                                removalView?.itemRemovalLabel(item.id), !state.blocksNavigation,
                                select = { select(item) })
                        }
                    }
                    detailPane(Modifier.weight(0.66f).fillMaxHeight())
                }
            } else {
                Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(definitions, key = { it.id }) { item ->
                            ItemChoice(item, item.id == selectedId, progress[item.id], history[item.id].orEmpty(),
                                removalView?.itemRemovalLabel(item.id), !state.blocksNavigation,
                                Modifier.width(208.dp), select = { select(item) })
                        }
                    }
                    detailPane(Modifier.weight(1f).fillMaxWidth())
                }
            }
        }
        val confirmation = itemRemoval
        if (confirmation != null && state.canStartRemoval(confirmation)) {
            ItemRemovalConfirmation(
                last = removalView?.items?.count { it.server != ItemDisposition.Removed } == 1,
                dismiss = { itemRemoval = null },
                save = { confirmed, source, reason ->
                    itemRemoval = null
                    model.saveWorkbenchItemRemoval(target, confirmation, confirmed, source, reason)
                },
            )
        } else if (confirmation != null) {
            LaunchedEffect(confirmation) { itemRemoval = null }
        }
        removal?.let { sample ->
            val attempt = progress[selected.id]?.attempt
            AlertDialog(
                onDismissRequest = { removal = null },
                title = { Text("Remove this sample?") },
                text = { Text("The sample will stop counting toward this item. Its original value and history will be retained.") },
                confirmButton = {
                    Button(onClick = {
                        removal = null
                        if (attempt != null) pilot.remove(target, attempt, sample)
                    }, enabled = attempt?.let(state::canEditSamples) == true) { Text("Confirm removal") }
                },
                dismissButton = { TextButton(onClick = { removal = null }) { Text("Cancel") } },
            )
        }
    }
}

@Composable
private fun WorkbenchHeading(origin: Screen.Detail, state: WorkbenchUi, online: Boolean, arm: () -> Unit,
                             reload: () -> Unit, switchLine: () -> Unit) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Surface(color = MaterialTheme.colorScheme.primaryContainer, shape = MaterialTheme.shapes.small) {
            Text("Development Pilot / Local Draft", Modifier.padding(horizontal = 10.dp, vertical = 5.dp),
                style = MaterialTheme.typography.labelLarge)
        }
        Text("Task " + origin.task.taskNo, style = MaterialTheme.typography.headlineSmall)
        Text(origin.line.name + " · " + origin.line.code, style = MaterialTheme.typography.titleSmall)
        state.draft?.let { draft ->
            val serverVersion = "Observed server draft " + draft.observedServerDraftVersion
            Text(if (state.hasLocalAccess) serverVersion + " · Local edit " + draft.localEditVersion else serverVersion,
                style = MaterialTheme.typography.bodySmall)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
            if (state.blocked == WorkProblem.StaleGrant) {
                Text("Online refresh required", style = MaterialTheme.typography.labelLarge)
            } else if (state.removalView(online)?.serverAllItemsRemoved == true) {
                Text("No applicable items remain", style = MaterialTheme.typography.labelLarge)
            } else if (state.blocked != null || !state.hasLocalAccess) {
                Button(onClick = arm, enabled = online && !state.blocksNavigation) { Text("Activate local capture") }
            } else {
                Text("Local capture active", style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary)
            }
            TextButton(onClick = reload, enabled = !state.blocksNavigation) { Text("Reload draft") }
            TextButton(onClick = switchLine, enabled = !state.blocksNavigation) { Text("Switch line") }
        }
        state.blocked?.let { Text(it.label(), style = MaterialTheme.typography.bodySmall) }
    }
}

@Composable
private fun ItemChoice(item: LineTaskDefinition, selected: Boolean, progress: LocalSampleProgress?,
                       history: List<LocalSampleHistory>, removalStatus: String?, enabled: Boolean, modifier: Modifier = Modifier,
                       select: () -> Unit) {
    val currentStatus = history.lastOrNull { it.currentAttempt && it.matchesObservedDraftVersion }?.operation?.state?.label()
    Card(onClick = select, modifier = modifier.fillMaxWidth().testTag("inspection-item-" + item.id)
        .semantics { this.selected = selected }, enabled = enabled,
        colors = CardDefaults.cardColors(containerColor = if (selected) MaterialTheme.colorScheme.primaryContainer
            else MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(item.inspectionItem, style = MaterialTheme.typography.titleSmall)
            Text(progress?.countLabel() ?: "No current sample requirement", style = MaterialTheme.typography.bodySmall)
            (removalStatus ?: currentStatus)?.let { Text(it, style = MaterialTheme.typography.labelMedium) }
        }
    }
}

@Composable
private fun WorkbenchItem(
    modifier: Modifier, item: LineTaskDefinition, progress: LocalSampleProgress?, state: WorkbenchUi, form: SampleForm,
    change: (SampleForm) -> Unit, save: (WorkAttempt, SampleInput) -> Unit,
    correct: (LocalSampleValue) -> Unit, remove: (LocalSampleValue) -> Unit,
    itemRemovalPanel: @Composable () -> Unit,
) {
    val type = progress?.type?.takeIf { it == FrozenSampleType.fromDefinition(item.inspectionType) }
    val canEditSamples = progress?.attempt?.let(state::canEditSamples) == true
    val current = state.draft?.effective.orEmpty().filter { it.key.attempt == progress?.attempt }
    val histories = state.draft?.history.orEmpty().asReversed().filter { it.operation.envelope.operation.attempt.taskItemId == item.id }
    val older = state.draft?.confirmed.orEmpty().filter { it.key.attempt.taskItemId == item.id && !it.current &&
        current.none { row -> row.key == it.key } }
    val scroll = rememberLazyListState()
    val scope = rememberCoroutineScope()
    LazyColumn(modifier.testTag("workbench-items"), state = scroll, contentPadding = PaddingValues(bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            ElevatedCard(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                    Text(item.inspectionItem, style = MaterialTheme.typography.titleLarge)
                    if (item.processName.isNotBlank()) Text(item.processName, style = MaterialTheme.typography.labelLarge)
                    if (item.inspectionContent.isNotBlank()) Text(item.inspectionContent)
                    if (item.samplingPlan.isNotBlank()) Text("Sampling plan · " + item.samplingPlan)
                    if (item.lowerLimitValue.isNotBlank()) Text("Frozen lower limit · " + item.lowerLimitOperator + " " + item.lowerLimitValue)
                    if (item.upperLimitValue.isNotBlank()) Text("Frozen upper limit · " + item.upperLimitOperator + " " + item.upperLimitValue)
                    Text(progress?.let { it.countLabel() + if (it.meetsCount) " · Count met" else " · Incomplete" }
                        ?: "No applicable current attempt", style = MaterialTheme.typography.titleSmall)
                    itemRemovalPanel()
                }
            }
        }
        if (type != null && state.hasLocalAccess) {
            item {
                val withinCapacity = progress.applicableSamples < MAX_CURRENT_SAMPLES || form.previous != null
                SampleEditor(type, form, change, enabled = canEditSamples && withinCapacity,
                    save = { input -> save(progress.attempt, input) })
                if (!withinCapacity) Text("50 applicable samples. Correct or remove an eligible sample.")
            }
        }
        item {
            Text("Current samples", style = MaterialTheme.typography.titleMedium)
            if (current.isEmpty()) Text("No samples recorded.")
        }
        items(current, key = { "sample-" + it.key.clientSampleId }) { sample ->
            Surface(Modifier.fillMaxWidth(), color = MaterialTheme.colorScheme.surface, shape = MaterialTheme.shapes.medium) {
                Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(if (sample.removed) "Sample removed" else sample.value.display(), style = MaterialTheme.typography.titleSmall)
                    Text(sample.status.label(), style = MaterialTheme.typography.labelMedium)
                    if (!sample.removed && sample.editablePredecessor != null && type != null) {
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            OutlinedButton(onClick = {
                                correct(sample)
                                scope.launch { scroll.animateScrollToItem(1) }
                            }, enabled = canEditSamples) { Text("Correct sample") }
                            TextButton(onClick = { remove(sample) }, enabled = canEditSamples) { Text("Remove sample") }
                        }
                    }
                }
            }
        }
        if (histories.isNotEmpty() || older.isNotEmpty()) {
            item {
                HorizontalDivider(Modifier.padding(vertical = 6.dp))
                Text("Sample history", style = MaterialTheme.typography.titleMedium)
            }
            items(histories, key = { "history-" + it.operation.envelope.operation.operationId }) { HistoryRow(it) }
            items(older, key = { "server-history-" + it.sampleId }) {
                Text("Server history · " + if (it.removed) "Sample removed" else it.value.inspectionValue ?: it.value.judgment)
            }
        }
    }
}

@Composable
private fun SampleEditor(type: FrozenSampleType, form: SampleForm, change: (SampleForm) -> Unit,
                         enabled: Boolean, save: (SampleInput) -> Unit) {
    val barcodeFocus = remember { FocusRequester() }
    val focus = LocalFocusManager.current
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(if (form.previous == null) "New sample" else "Correct sample", style = MaterialTheme.typography.titleMedium)
            when (type) {
                FrozenSampleType.Qualitative -> Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    QualitativeJudgment.entries.forEach { option ->
                        FilterChip(selected = form.judgment == option, enabled = enabled,
                            onClick = { change(form.copy(judgment = option)) }, label = { Text(option.name) })
                    }
                }
                FrozenSampleType.Quantitative -> {
                    OutlinedTextField(value = form.decimal, onValueChange = { change(form.copy(decimal = it)) },
                        enabled = enabled, modifier = Modifier.fillMaxWidth().testTag("inspection-value"), label = { Text("Inspection value") },
                        singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii, imeAction = ImeAction.Next),
                        keyboardActions = KeyboardActions(onNext = { barcodeFocus.requestFocus() }))
                    OutlinedTextField(value = form.barcode, onValueChange = { change(form.copy(barcode = it)) },
                        enabled = enabled, modifier = Modifier.fillMaxWidth().focusRequester(barcodeFocus).testTag("inspection-barcode"),
                        label = { Text("Barcode (optional)") }, singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii, imeAction = ImeAction.Done),
                        keyboardActions = KeyboardActions(onDone = { focus.clearFocus() }))
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                val input = form.input(type)
                Button(onClick = { input?.let(save) }, enabled = enabled && input != null) { Text("Save locally") }
                if (form.previous != null) {
                    TextButton(onClick = { change(SampleForm()) }, enabled = enabled) { Text("Cancel correction") }
                }
            }
        }
    }
}

@Composable
private fun HistoryRow(entry: LocalSampleHistory) {
    val operation = entry.operation
    val input = operation.envelope.operation
    val kind = when (input.kind) {
        SampleKind.CaptureSample -> "Captured"
        SampleKind.CorrectSample -> "Corrected"
        SampleKind.RemoveSample -> "Removed"
    }
    Column(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
        Text(kind + " · " + operation.state.label(), style = MaterialTheme.typography.titleSmall)
        if (input.data != null) Text(input.data.display())
        Text("Contributor " + operation.envelope.source.contributorUserId, style = MaterialTheme.typography.bodySmall)
        if (!entry.currentAttempt) Text("Historical attempt", style = MaterialTheme.typography.labelMedium)
        else if (!entry.matchesObservedDraftVersion && entry.acceptedReceipt == null) {
            Text("Earlier draft version", style = MaterialTheme.typography.labelMedium)
        }
    }
}

@Composable
private fun WorkbenchNotice(message: String, error: Boolean = false) {
    Surface(Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite },
        color = if (error) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer,
        shape = MaterialTheme.shapes.small) {
        Text(message, Modifier.padding(8.dp), style = MaterialTheme.typography.bodyMedium)
    }
}

private fun LocalSampleProgress.countLabel() = "$applicableSamples / $required applicable samples"

private fun SamplePayload?.display(): String = if (this == null) "No value" else
    listOfNotNull(inspectionValue, judgment, barcode?.let { "Barcode " + it }).joinToString(" · ")
