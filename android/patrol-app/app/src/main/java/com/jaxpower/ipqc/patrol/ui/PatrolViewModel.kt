package com.jaxpower.ipqc.patrol.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PagedLineTasks
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.auth.Operator
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch

sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Data<T>(val value: T) : Load<T>
    data class Failed(val issue: Issue) : Load<Nothing>
}

sealed interface Screen {
    data object Login : Screen
    data class ChangePassword(val operator: Operator) : Screen
    data class Home(val operator: Operator) : Screen
    data class Lines(val operator: Operator, val load: Load<List<ProductionLine>>) : Screen
    data class Tasks(val operator: Operator, val line: ProductionLine, val page: Int, val load: Load<PagedLineTasks>) : Screen
    data class Detail(val operator: Operator, val line: ProductionLine, val task: LineTaskSummary,
                      val listPage: Int, val load: Load<LineTaskDetail>) : Screen
}

data class PatrolUi(
    val screen: Screen = Screen.Login,
    val online: Boolean = false,
    val configured: Boolean = true,
    val busy: Boolean = false,
    val issue: Issue? = null,
    val message: String? = null,
)

class PatrolViewModel(
    private val sessions: SessionRepository?,
    private val patrol: PatrolRepository?,
    connectivity: Flow<Boolean>,
) : ViewModel() {
    private val mutableUi = MutableStateFlow(PatrolUi(configured = sessions != null, issue = if (sessions == null) Issue.CONFIGURATION else null))
    val ui = mutableUi.asStateFlow()
    private var pending: Job? = null
    private var epoch = 0
    private var signingOut = false

    init {
        viewModelScope.launch {
            var first = true
            connectivity.distinctUntilChanged().collect { online ->
                mutableUi.value = ui.value.copy(online = online)
                if (sessions != null && !online && !signingOut) {
                    interrupt()
                    show(Screen.Login, issue = Issue.OFFLINE)
                } else if (first && online && sessions != null) {
                    resume()
                } else if (online && ui.value.issue == Issue.OFFLINE) {
                    show(Screen.Login)
                }
                first = false
            }
        }
    }

    fun resume() {
        if (ui.value.screen != Screen.Login) return
        val service = sessions ?: return
        run(Screen.Login) { routeFor(service.restore()) }
    }

    fun login(username: String, password: String) {
        if (ui.value.screen != Screen.Login || username.isBlank() || password.isEmpty()) return
        val service = sessions ?: return
        run(Screen.Login) { routeFor(service.login(username.trim(), password)) }
    }

    fun changePassword(current: String, replacement: String) {
        val screen = ui.value.screen as? Screen.ChangePassword ?: return
        if (current.isEmpty() || replacement.isEmpty()) return
        val service = sessions ?: return
        run(screen, message = "Password changed. Sign in again.") {
            service.changePassword(current, replacement)
            Screen.Login
        }
    }

    private fun routeFor(operator: Operator?): Screen = when {
        operator == null -> Screen.Login
        operator.mustChangePassword -> Screen.ChangePassword(operator)
        else -> Screen.Home(operator)
    }

    fun openPatrol() {
        val home = ui.value.screen as? Screen.Home ?: return
        loadLines(home.operator)
    }

    fun select(line: ProductionLine) {
        val lines = ui.value.screen as? Screen.Lines ?: return
        if ((lines.load as? Load.Data)?.value?.contains(line) != true) return
        loadTasks(lines.operator, line, 1)
    }

    fun switchLine() {
        when (val screen = ui.value.screen) {
            is Screen.Tasks -> loadLines(screen.operator)
            is Screen.Detail -> loadLines(screen.operator)
            else -> Unit
        }
    }

    fun nextPage() {
        val screen = ui.value.screen as? Screen.Tasks ?: return
        if ((screen.load as? Load.Data)?.value?.hasNext == true) loadTasks(screen.operator, screen.line, screen.page + 1)
    }

    fun previousPage() {
        val screen = ui.value.screen as? Screen.Tasks ?: return
        if (screen.page > 1) loadTasks(screen.operator, screen.line, screen.page - 1)
    }

    fun openTask(task: LineTaskSummary) {
        val screen = ui.value.screen as? Screen.Tasks ?: return
        if ((screen.load as? Load.Data)?.value?.items?.contains(task) != true) return
        loadDetail(Screen.Detail(screen.operator, screen.line, task, screen.page, Load.Loading))
    }

    fun refresh() {
        when (val screen = ui.value.screen) {
            is Screen.Lines -> loadLines(screen.operator)
            is Screen.Tasks -> loadTasks(screen.operator, screen.line, screen.page)
            is Screen.Detail -> loadDetail(screen.copy(load = Load.Loading))
            else -> Unit
        }
    }

    fun back() {
        if (ui.value.busy) return
        when (val screen = ui.value.screen) {
            is Screen.Detail -> loadTasks(screen.operator, screen.line, screen.listPage)
            is Screen.Tasks -> loadLines(screen.operator)
            is Screen.Lines -> show(Screen.Home(screen.operator))
            else -> Unit
        }
    }

    fun logout() {
        if (signingOut || (ui.value.screen == Screen.Login && ui.value.busy)) return
        val service = sessions ?: return
        interrupt()
        signingOut = true
        show(Screen.Login, busy = true)
        pending = viewModelScope.launch {
            try {
                val confirmed = service.logout(attemptRemote = ui.value.online)
                show(Screen.Login, message = if (confirmed) "Signed out."
                    else "Signed out on this tablet. Server revocation could not be confirmed.")
            } catch (failure: PatrolFailure) {
                show(Screen.Login, issue = failure.issue, message = "Patrol access has ended on this tablet. Server revocation could not be confirmed.")
            } finally {
                signingOut = false
            }
        }
    }

    private fun loadLines(operator: Operator) = run(Screen.Lines(operator, Load.Loading)) {
        Screen.Lines(operator, Load.Data(requireNotNull(patrol).lines()))
    }

    private fun loadTasks(operator: Operator, line: ProductionLine, page: Int) =
        run(Screen.Tasks(operator, line, page, Load.Loading)) {
            Screen.Tasks(operator, line, page, Load.Data(requireNotNull(patrol).tasks(line, page)))
        }

    private fun loadDetail(screen: Screen.Detail) = run(screen) {
        screen.copy(load = Load.Data(requireNotNull(patrol).detail(screen.line, screen.task)))
    }

    private fun run(screen: Screen, message: String? = null, action: suspend () -> Screen) {
        if (ui.value.busy || signingOut || !ui.value.online || !ui.value.configured) return
        val stamp = ++epoch
        show(screen, busy = true)
        pending = viewModelScope.launch {
            try {
                val result = action()
                if (stamp == epoch) show(result, message = message)
            } catch (e: PatrolFailure) {
                if (stamp != epoch) return@launch
                val issue = e.issue
                when {
                    issue in END_SESSION -> show(Screen.Login, issue = issue)
                    screen is Screen.Lines -> show(screen.copy(load = Load.Failed(issue)))
                    screen is Screen.Tasks -> show(screen.copy(load = Load.Failed(issue)))
                    screen is Screen.Detail -> show(screen.copy(load = Load.Failed(issue)))
                    else -> show(screen, issue = issue)
                }
            }
        }
    }

    private fun interrupt() {
        epoch++
        pending?.cancel()
        pending = null
    }

    private fun show(screen: Screen, busy: Boolean = false, issue: Issue? = null, message: String? = null) {
        mutableUi.value = ui.value.copy(screen = screen, busy = busy, issue = issue, message = message)
    }

    private companion object {
        val END_SESSION = setOf(Issue.UNAUTHENTICATED, Issue.FORBIDDEN, Issue.REAUTHENTICATE, Issue.STORAGE)
    }
}
