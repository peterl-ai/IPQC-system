package com.jaxpower.ipqc.patrol.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.jaxpower.ipqc.patrol.work.BasicInformationField
import com.jaxpower.ipqc.patrol.work.BasicInformationReceipt
import com.jaxpower.ipqc.patrol.work.ReceiptKind
import com.jaxpower.ipqc.patrol.work.WorkConflict
import com.jaxpower.ipqc.patrol.work.WorkState
import com.jaxpower.ipqc.patrol.work.WorkbenchBasicInformation
import com.jaxpower.ipqc.patrol.work.unresolvedAttempt
import com.jaxpower.ipqc.patrol.work.validBasicInformationValue

@Composable
internal fun BasicInformationEditor(
    modifier: Modifier, state: WorkbenchUi, model: PatrolViewModel, target: WorkbenchTarget, online: Boolean,
) {
    val header = state.header
    val field = state.headerField
    val fact = header?.observation?.basis?.field(field)
    var input by remember(target, field, header?.observation?.basis, state.headerSavedId) { mutableStateOf("") }
    var explicitNull by remember(target, field, header?.observation?.basis, state.headerSavedId) { mutableStateOf(false) }
    val valid = validBasicInformationValue(input)
    val fresh = !state.headerRefreshRequired && header != null
    val actionable = fresh && !state.blocksNavigation
    val editable = actionable && header?.canSave(field) == true
    val focus = LocalFocusManager.current

    Column(modifier.testTag("basic-information-editor"), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        state.headerResult?.let { Text(it, modifier = Modifier.testTag("basic-information-result")) }
        if (state.headerRefreshRequired && !state.busy)
            Text("Reload the draft to verify Basic Information.", modifier = Modifier.testTag("basic-information-refresh-required"))
        if (state.headerSavePending) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { model.checkBasicInformationSave(target) }, enabled = !state.busy) {
                    Text("Check Basic Information save")
                }
                Button(onClick = { model.retryBasicInformationSave(target) }, enabled = !state.busy) {
                    Text("Retry same field save")
                }
            }
        }

        val fieldChoices: @Composable () -> Unit = {
            for (choice in BasicInformationField.entries) {
                OutlinedButton(onClick = {
                    focus.clearFocus()
                    model.selectBasicInformationField(target, choice)
                },
                    enabled = !state.blocksNavigation && choice != field,
                    modifier = Modifier.testTag("basic-information-field-${choice.name}")) {
                    Text(choice.label())
                }
            }
        }
        val detail: @Composable (Modifier) -> Unit = { pane ->
            LazyColumn(pane, contentPadding = PaddingValues(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)) {
                item {
                    Text(field.label(), style = MaterialTheme.typography.headlineSmall)
                    if (fact != null) {
                        Text("Draft version ${header.observation.basis.draftVersion}", style = MaterialTheme.typography.bodySmall)
                        val label = if (fresh && online) "Server-confirmed" else "Last verified"
                        Text("$label · ${fact.accepted?.operation?.value.display(fact.accepted != null)}",
                            modifier = Modifier.testTag("basic-information-confirmed"))
                        fact.accepted?.let { Text(it.attribution(), style = MaterialTheme.typography.bodySmall) }
                    }
                }
                if (editable) {
                    item {
                        Text("New local proposal", style = MaterialTheme.typography.titleMedium)
                        OutlinedTextField(value = input, onValueChange = { input = it }, modifier = Modifier.fillMaxWidth()
                            .testTag("basic-information-value"), label = { Text("${field.label()} value") },
                            enabled = !explicitNull, isError = !valid, singleLine = true,
                            supportingText = {
                                Text(if (valid) "Optional · up to 200 Unicode characters" else
                                    "Use at most 200 Unicode characters without control characters.")
                            })
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = {
                                explicitNull = !explicitNull
                                focus.clearFocus()
                            }) {
                                Text(if (explicitNull) "Use text value" else "Use explicit null")
                            }
                            Button(onClick = {
                                focus.clearFocus()
                                model.saveBasicInformation(target, header, field, if (explicitNull) null else input)
                            }, enabled = explicitNull || valid) { Text("Save field locally") }
                        }
                        if (explicitNull) Text("The proposal records an explicit null.")
                    }
                }
                if (fresh) {
                    val proposals = header.proposals.filter { it.field == field }.asReversed()
                    if (proposals.isNotEmpty()) item { Text("Local history", style = MaterialTheme.typography.titleMedium) }
                    items(proposals, key = { it.operation.operationId }) { row ->
                        BasicInformationProposal(row, actionable && online,
                            upload = { model.uploadBasicInformation(target, header, row) },
                            receipt = { model.checkBasicInformationReceipt(target, header, row, it) })
                    }
                    val history = header.observation.history.filter { it.operation.field == field }
                    if (history.isNotEmpty()) item { Text("Accepted field history", style = MaterialTheme.typography.titleMedium) }
                    items(history, key = { "history-${it.operation.operationId}" }) { receipt ->
                        Column {
                            Text("Version ${receipt.draftVersion} · ${receipt.operation.value.display()}")
                            Text(receipt.attribution(), style = MaterialTheme.typography.bodySmall)
                        }
                    }
                    if (online && header.observation.historyBeforeDraftVersion != null) item {
                        OutlinedButton(onClick = { model.loadOlderBasicInformation(target, header) }, enabled = actionable) {
                            Text("Load older field history")
                        }
                    }
                }
            }
        }

        BoxWithConstraints(Modifier.fillMaxWidth().weight(1f)) {
            if (maxWidth >= 760.dp) {
                Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                    Column(Modifier.weight(0.3f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        fieldChoices()
                    }
                    detail(Modifier.weight(0.7f).fillMaxHeight())
                }
            } else {
                Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) { fieldChoices() }
                    detail(Modifier.fillMaxWidth().weight(1f))
                }
            }
        }
    }
}

@Composable
private fun BasicInformationProposal(
    row: WorkbenchBasicInformation.Row, enabled: Boolean, upload: () -> Unit, receipt: (ReceiptKind) -> Unit,
) {
    val status = when {
        row.state == WorkState.Accepted -> "Server confirmed"
        row.state.unresolvedAttempt() -> "Outcome unknown"
        row.state == WorkState.Quarantined -> "Source quarantined"
        row.state == WorkState.Conflict && row.conflict != WorkConflict.DraftVersionConflict -> "Conflict"
        row.stale || row.conflict == WorkConflict.DraftVersionConflict -> "Stale local proposal"
        else -> "Local proposal"
    }
    Card(Modifier.fillMaxWidth().testTag("basic-information-proposal")) {
        Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("$status · ${row.value.display()}", style = MaterialTheme.typography.titleSmall)
            Text("Based on draft version ${row.baseDraftVersion} · Contributor ${row.contributorUserId}",
                style = MaterialTheme.typography.bodySmall)
            row.confirmed?.let { Text(it.attribution(), style = MaterialTheme.typography.bodySmall) }
            if (row.state == WorkState.Queued && row.stale)
                Text("The original is retained. Review the current field before saving a new proposal.")
            if (row.canUpload) Button(onClick = upload, enabled = enabled) { Text("Upload field") }
            if (row.state.unresolvedAttempt()) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { receipt(ReceiptKind.Batch) }, enabled = enabled) { Text("Check batch receipt") }
                    OutlinedButton(onClick = { receipt(ReceiptKind.Operation) }, enabled = enabled) { Text("Check field receipt") }
                }
            }
        }
    }
}

private fun BasicInformationField.label(): String = if (this == BasicInformationField.ProductMo) "Product MO" else name

private fun String?.display(recorded: Boolean = true): String = when {
    !recorded -> "Never recorded"
    this == null -> "Explicit null"
    isEmpty() -> "Empty"
    all { Character.isSpaceChar(it) } -> "Whitespace only"
    else -> this
}

private fun BasicInformationReceipt.attribution(): String =
    "Contributor ${operation.contributorUserId} · Accepted by $actorUserId · Session $actorPersonalSessionId · $acceptedAtUtc"
