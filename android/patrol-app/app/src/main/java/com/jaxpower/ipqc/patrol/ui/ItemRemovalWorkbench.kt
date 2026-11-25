package com.jaxpower.ipqc.patrol.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.jaxpower.ipqc.patrol.work.ItemDisposition
import com.jaxpower.ipqc.patrol.work.RemovalConfirmation
import com.jaxpower.ipqc.patrol.work.WorkState
import com.jaxpower.ipqc.patrol.work.WorkbenchRemoval
import com.jaxpower.ipqc.patrol.work.uncertainItemRemovalStates
import com.jaxpower.ipqc.patrol.work.validRemovalReason

internal fun WorkbenchRemoval.itemRemovalLabel(itemId: String): String? {
    val item = items.singleOrNull { it.attempt.taskItemId == itemId }
    val current = rows.lastOrNull { it.attempt == item?.attempt }
    return when {
        current?.state in uncertainItemRemovalStates -> current?.state?.removalLabel()
        item?.server == ItemDisposition.Removed || current?.state == WorkState.Accepted -> WorkState.Accepted.removalLabel()
        else -> current?.state?.removalLabel()
    }
}

@Composable
internal fun ItemRemovalPanel(itemId: String, view: WorkbenchRemoval?, online: Boolean,
                              busy: Boolean, stale: Boolean, canStart: Boolean, start: () -> Unit,
                              upload: (WorkbenchRemoval.Row) -> Unit, checkReceipt: (WorkbenchRemoval.Row) -> Unit) {
    val item = view?.items?.singleOrNull { it.attempt.taskItemId == itemId }
    val rows = view?.rows.orEmpty().filter { it.attempt.taskItemId == itemId }
    if (item == null && rows.isEmpty()) return

    Column(Modifier.testTag("item-removal-status"), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (item?.server == ItemDisposition.Removed && rows.none { it.currentAttempt && it.state == WorkState.Accepted }) {
            Text("Server-confirmed Removed", style = MaterialTheme.typography.titleSmall)
        } else if (item?.server == ItemDisposition.Applicable && rows.none { it.currentAttempt }) {
            Text("Applicable", style = MaterialTheme.typography.labelLarge)
        }
        for (row in rows.asReversed()) {
            val status = row.state.removalLabel()
            Text(if (row.currentAttempt) status else "Previous attempt · " + status,
                style = MaterialTheme.typography.labelLarge)
            if (row.state in uncertainItemRemovalStates && row.operationConfirmed) {
                Text("Item removal verified. The batch receipt is still required.")
            }
            if (row.state == WorkState.Queued && row.currentAttempt && !row.operationConfirmed) {
                OutlinedButton(onClick = { upload(row) }, enabled = online && !busy && !stale &&
                    view?.requiresRefresh == false) { Text("Upload removal") }
            }
            if (row.state in uncertainItemRemovalStates && row.batchId != null) {
                OutlinedButton(onClick = { checkReceipt(row) }, enabled = online && !busy) { Text("Check removal receipt") }
            }
        }
        if (view?.serverAllItemsRemoved == true || item?.warnedLast == true ||
            rows.any { it.currentAttempt && it.warnedLast }) {
            Text("Server confirmed that no applicable items remained at the last removal. No inspection outcome was submitted.",
                style = MaterialTheme.typography.bodySmall)
        } else if (view?.provisionallyAllItemsRemoved == true && rows.any { it.currentAttempt && it.state == WorkState.Queued }) {
            Text("Local projection: the last applicable item is queued for removal.",
                style = MaterialTheme.typography.bodySmall)
        }
        if (canStart) {
            OutlinedButton(onClick = start, modifier = Modifier.testTag("remove-inspection-item")) { Text("Remove item") }
        }
    }
}

@Composable
internal fun ItemRemovalConfirmation(last: Boolean, dismiss: () -> Unit,
                                     save: (Boolean, RemovalConfirmation, String?) -> Unit) {
    var confirmed by remember { mutableStateOf(false) }
    var source by remember { mutableStateOf<RemovalConfirmation?>(null) }
    var reason by remember { mutableStateOf<String?>(null) }
    val invalid = !validRemovalReason(reason)
    AlertDialog(
        modifier = Modifier.testTag("item-removal-confirmation"),
        onDismissRequest = dismiss,
        title = { Text("Remove inspection item?") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (last) {
                    Text("This is the last applicable item. Saving its removal does not submit an inspection outcome.")
                }
                Row(Modifier.fillMaxWidth().toggleable(confirmed, role = Role.Checkbox, onValueChange = { confirmed = it })
                    .testTag("confirm-item-removal"), verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = confirmed, onCheckedChange = null)
                    Text("I have confirmed with Production or Equipment that this inspection item is absent or not applicable.")
                }
                Text("Confirmation source", style = MaterialTheme.typography.labelLarge)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    RemovalConfirmation.entries.forEach { option ->
                        FilterChip(selected = source == option, onClick = { source = option }, label = { Text(option.name) })
                    }
                }
                OutlinedTextField(value = reason.orEmpty(), onValueChange = { reason = it }, isError = invalid,
                    modifier = Modifier.fillMaxWidth().testTag("item-removal-reason"), singleLine = true,
                    label = { Text("Reason (optional)") },
                    supportingText = if (invalid) {
                        { Text("Use at most 500 characters and no control characters.") }
                    } else null)
            }
        },
        confirmButton = {
            Button(onClick = { source?.let { save(confirmed, it, reason) } },
                enabled = confirmed && source != null && !invalid) { Text("Save removal") }
        },
        dismissButton = { TextButton(onClick = dismiss) { Text("Cancel") } },
    )
}

private fun WorkState.removalLabel(): String = when (this) {
    WorkState.Queued -> "Removal saved locally"
    WorkState.InFlight -> "Upload interrupted or in flight · Check receipt"
    WorkState.OutcomeUnknown -> "Upload outcome unknown · Check receipt"
    WorkState.Accepted -> "Server-confirmed Removed"
    WorkState.Conflict -> "Removal conflict · Original kept"
    WorkState.Quarantined -> "Removal quarantined · Source no longer eligible"
}
