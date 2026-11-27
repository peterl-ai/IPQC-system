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
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.offline.OfflineSession
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.work.OnlineWorkCoordinator
import com.jaxpower.ipqc.patrol.work.RemovalConfirmation
import com.jaxpower.ipqc.patrol.work.WorkAttempt
import com.jaxpower.ipqc.patrol.work.BasicInformationField
import com.jaxpower.ipqc.patrol.work.ReceiptKind
import com.jaxpower.ipqc.patrol.work.WorkbenchBasicInformation
import com.jaxpower.ipqc.patrol.work.WorkbenchRemoval
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
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
    data class SampleWorkbench(val origin: Detail) : Screen
}

data class PatrolUi(
    val screen: Screen = Screen.Login,
    val online: Boolean = false,
    val configured: Boolean = true,
    val busy: Boolean = false,
    val issue: Issue? = null,
    val message: String? = null,
    val offline: OfflineSession? = null,
)

class PatrolViewModel internal constructor(
    private val sessions: SessionRepository?,
    private val patrol: PatrolRepository?,
    connectivity: Flow<Boolean>,
    private val cache: OfflineCache?,
    onlineWorkbench: OnlineWorkCoordinator?,
) : ViewModel() {
    constructor(sessions: SessionRepository?, patrol: PatrolRepository?, connectivity: Flow<Boolean>,
                cache: OfflineCache? = null) : this(sessions, patrol, connectivity, cache, null)

    internal val workbench = onlineWorkbench?.let { SampleWorkbenchModel(viewModelScope, it) }
    private val mutableUi = MutableStateFlow(PatrolUi(configured = sessions != null, issue = if (sessions == null) Issue.CONFIGURATION else null))
    val ui = mutableUi.asStateFlow()
    private var pending: Job? = null
    private var epoch = 0
    private var signingOut = false
    private var networkKnown = false
    private var foreground = true
    private var recheck: Job? = null
    private var expiryCheck: Job? = null

    init {
        viewModelScope.launch {
            var first = true
            connectivity.distinctUntilChanged().collect { online ->
                mutableUi.value = ui.value.copy(online = online)
                networkKnown = true
                if (sessions != null && !signingOut) {
                    if (!foreground) {
                        if (cache != null) {
                            interrupt()
                            show(Screen.Login)
                        }
                    } else if (!online) {
                        enterOffline()
                    } else if (ui.value.screen is Screen.SampleWorkbench && ui.value.offline != null) {
                        // The pilot can retain the local draft on network return.
                        // Existing explicit verification is required before arming online again.
                    } else if (first || cache != null) {
                        reconnect()
                    } else if (ui.value.issue == Issue.OFFLINE) {
                        show(Screen.Login)
                    }
                }
                first = false
            }
        }
    }

    fun resume() {
        if (ui.value.screen != Screen.Login) return
        val service = sessions ?: return
        run(Screen.Login, allowOfflineFallback = true) {
            routeFor(service.restore())
        }
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
            is Screen.SampleWorkbench -> {
                if (workbench?.ui?.value?.blocksNavigation != true) loadLines(screen.origin.operator)
            }
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

    internal fun openWorkbench() {
        val screen = ui.value.screen as? Screen.Detail ?: return
        val pilot = workbench ?: return
        if (ui.value.busy || !foreground || signingOut) return
        val target = WorkbenchTarget.from(screen) ?: return
        val local = ui.value.offline
        if (local != null && local.identity != target.owner) return
        show(Screen.SampleWorkbench(screen))
        if (ui.value.screen is Screen.SampleWorkbench) pilot.open(target, canUseWorkbenchNetwork())
    }

    internal fun armWorkbench(target: WorkbenchTarget) {
        if (canUseWorkbenchNetwork()) workbench?.arm(target)
    }

    internal fun reloadWorkbench(target: WorkbenchTarget) {
        if (foreground && !ui.value.busy && ui.value.screen is Screen.SampleWorkbench) {
            workbench?.reload(target, canUseWorkbenchNetwork())
        }
    }

    internal fun saveWorkbenchItemRemoval(target: WorkbenchTarget, attempt: WorkAttempt, confirmed: Boolean,
                                          source: RemovalConfirmation, reason: String?) {
        if (canUseWorkbench()) workbench?.saveItemRemoval(target, attempt, confirmed, source, reason)
    }

    internal fun checkWorkbenchRemovalSave(target: WorkbenchTarget) {
        if (canUseWorkbench()) workbench?.checkItemRemovalSave(target)
    }

    internal fun retryWorkbenchRemovalSave(target: WorkbenchTarget) {
        if (canUseWorkbench()) workbench?.retryItemRemovalSave(target)
    }

    internal fun uploadWorkbenchItemRemoval(target: WorkbenchTarget, row: WorkbenchRemoval.Row) {
        if (canUseWorkbenchNetwork()) workbench?.uploadItemRemoval(target, row)
    }

    internal fun checkWorkbenchItemRemovalReceipt(target: WorkbenchTarget, row: WorkbenchRemoval.Row) {
        if (canUseWorkbenchNetwork()) workbench?.checkItemRemovalReceipt(target, row)
    }

    internal fun showBasicInformation(target: WorkbenchTarget, show: Boolean) {
        if (canUseWorkbench()) workbench?.showBasicInformation(target, show, canUseWorkbenchNetwork())
    }

    internal fun selectBasicInformationField(target: WorkbenchTarget, field: BasicInformationField) {
        if (canUseWorkbench()) workbench?.selectBasicInformationField(target, field)
    }

    internal fun saveBasicInformation(target: WorkbenchTarget, view: WorkbenchBasicInformation, field: BasicInformationField,
                                      value: String?) {
        if (canUseWorkbench()) workbench?.saveBasicInformation(target, view, field, value, canUseWorkbenchNetwork())
    }

    internal fun checkBasicInformationSave(target: WorkbenchTarget) {
        if (canUseWorkbench()) workbench?.checkBasicInformationSave(target, canUseWorkbenchNetwork())
    }

    internal fun retryBasicInformationSave(target: WorkbenchTarget) {
        if (canUseWorkbench()) workbench?.retryBasicInformationSave(target, canUseWorkbenchNetwork())
    }

    internal fun uploadBasicInformation(target: WorkbenchTarget, view: WorkbenchBasicInformation,
                                        row: WorkbenchBasicInformation.Row) {
        if (canUseWorkbenchNetwork()) workbench?.uploadBasicInformation(target, view, row)
    }

    internal fun checkBasicInformationReceipt(target: WorkbenchTarget, view: WorkbenchBasicInformation,
                                              row: WorkbenchBasicInformation.Row, kind: ReceiptKind) {
        if (canUseWorkbenchNetwork()) workbench?.checkBasicInformationReceipt(target, view, row, kind)
    }

    internal fun loadOlderBasicInformation(target: WorkbenchTarget, view: WorkbenchBasicInformation) {
        if (canUseWorkbenchNetwork()) workbench?.loadOlderBasicInformation(target, view)
    }

    private fun canUseWorkbench(): Boolean = foreground && !signingOut && !ui.value.busy &&
        ui.value.screen is Screen.SampleWorkbench

    private fun canUseWorkbenchNetwork(): Boolean = canUseWorkbench() && ui.value.online && ui.value.offline == null

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
            is Screen.SampleWorkbench -> {
                if (workbench?.ui?.value?.blocksNavigation != true) show(screen.origin)
            }
            is Screen.Tasks -> loadLines(screen.operator)
            is Screen.Lines -> show(Screen.Home(screen.operator))
            else -> Unit
        }
    }

    fun logout() {
        if (signingOut || (ui.value.screen == Screen.Login && ui.value.busy)) return
        val service = sessions ?: return
        val remote = ui.value.online && ui.value.offline == null
        interrupt()
        signingOut = true
        show(Screen.Login, busy = true)
        pending = viewModelScope.launch {
            try {
                val confirmed = service.logout(attemptRemote = remote)
                show(Screen.Login, message = if (confirmed) "Signed out."
                    else "Signed out on this tablet. Server revocation could not be confirmed.")
            } catch (failure: PatrolFailure) {
                show(Screen.Login, issue = failure.issue, message = "Sign-out could not be confirmed. Try clearing saved sign-in again.")
            } finally {
                signingOut = false
            }
        }
    }

    internal fun returnToForeground() {
        if (foreground) return
        foreground = true
        if (!networkKnown || sessions == null || signingOut || cache == null || ui.value.screen != Screen.Login) return
        if (ui.value.online) reconnect() else enterOffline()
    }

    internal fun leaveForeground() {
        foreground = false
        if (cache != null && !signingOut &&
            (ui.value.offline != null || !ui.value.online || ui.value.busy || ui.value.screen is Screen.SampleWorkbench)) {
            interrupt()
            show(Screen.Login)
        }
    }

    fun reconnect() {
        if (!ui.value.online || !foreground || signingOut) return
        interrupt()
        show(Screen.Login)
        resume()
    }

    private fun enterOffline(fallback: Issue? = Issue.OFFLINE) {
        val retained = ui.value.screen as? Screen.SampleWorkbench
        val expected = workbench?.ui?.value?.target?.owner
        workbench?.pauseBasicInformation()
        interrupt()
        val service = sessions
        val localCache = cache
        if (service == null || localCache == null || !foreground) {
            show(Screen.Login, issue = fallback)
            return
        }
        val stamp = epoch
        show(retained ?: Screen.Login, busy = true, issue = fallback)
        pending = viewModelScope.launch {
            try {
                val grant = service.resumeOffline()
                if (stamp != epoch) return@launch
                if (grant == null) {
                    show(Screen.Login, issue = fallback)
                } else if (localCache.remainingMs(grant) <= 0) {
                    lockOffline(Issue.OFFLINE_EXPIRED)
                } else {
                    val sameWorkbench = retained != null && expected == grant.identity
                    if (!sameWorkbench && retained != null) workbench?.close()
                    mutableUi.value = ui.value.copy(
                        screen = if (sameWorkbench) requireNotNull(retained) else Screen.Home(grant.operator),
                        busy = false, issue = null, message = null, offline = grant)
                    monitorOffline(localCache)
                }
            } catch (failure: PatrolFailure) {
                if (stamp == epoch) show(Screen.Login, issue = failure.issue)
            }
        }
    }

    private fun monitorOffline(service: OfflineCache) {
        expiryCheck = viewModelScope.launch {
            while (true) {
                val current = ui.value.offline ?: return@launch
                val remaining = service.remainingMs(current)
                if (remaining <= 0) {
                    lockOffline(Issue.OFFLINE_EXPIRED)
                    return@launch
                }
                delay(remaining.coerceAtMost(1_000))
            }
        }
        recheck = viewModelScope.launch {
            while (true) {
                val current = ui.value.offline ?: return@launch
                delay(1_000)
                try {
                    val observed = service.resume(current.identity) ?: throw PatrolFailure(Issue.OFFLINE)
                    if (ui.value.offline?.identity == current.identity) {
                        if (service.remainingMs(observed) <= 0) lockOffline(Issue.OFFLINE_EXPIRED)
                        else mutableUi.value = ui.value.copy(offline = observed)
                    }
                } catch (failure: PatrolFailure) {
                    if (ui.value.offline?.identity == current.identity) {
                        lockOffline(failure.issue)
                    }
                    return@launch
                }
            }
        }
    }

    private fun loadLines(operator: Operator) {
        val local = ui.value.offline
        run(Screen.Lines(operator, Load.Loading), readCache = true, allowOfflineFallback = true) {
            val lines = if (local != null) requireNotNull(cache).lines(local.identity) else requireNotNull(patrol).lines()
            Screen.Lines(operator, Load.Data(lines))
        }
    }

    private fun loadTasks(operator: Operator, line: ProductionLine, page: Int) {
        val local = ui.value.offline
        run(Screen.Tasks(operator, line, page, Load.Loading), readCache = true, allowOfflineFallback = true) {
            val tasks = if (local != null) requireNotNull(cache).tasks(local.identity, line, page)
                else requireNotNull(patrol).tasks(line, page)
            Screen.Tasks(operator, line, page, Load.Data(tasks))
        }
    }

    private fun loadDetail(screen: Screen.Detail) {
        val local = ui.value.offline
        run(screen, readCache = true, allowOfflineFallback = true) {
            val detail = if (local != null) requireNotNull(cache).detail(local.identity, screen.line, screen.task, screen.listPage)
                else requireNotNull(patrol).detail(screen.line, screen.task)
            screen.copy(load = Load.Data(detail))
        }
    }

    private fun run(
        screen: Screen,
        message: String? = null,
        readCache: Boolean = false,
        allowOfflineFallback: Boolean = false,
        action: suspend () -> Screen,
    ) {
        val local = ui.value.offline
        if (ui.value.busy || signingOut || !ui.value.configured || !foreground ||
            (if (local == null) !ui.value.online else !readCache)) return
        val stamp = ++epoch
        show(screen, busy = true)
        if (stamp != epoch) return
        pending = viewModelScope.launch {
            try {
                val result = action()
                if (stamp == epoch) {
                    if (result == Screen.Login && allowOfflineFallback && cache != null)
                        enterOffline(fallback = null)
                    else show(result, message = message)
                }
            } catch (e: PatrolFailure) {
                if (stamp != epoch) return@launch
                val issue = e.issue
                when {
                    local == null && cache != null && allowOfflineFallback && e.canUseOffline -> enterOffline(issue)
                    issue in END_SESSION || (local != null && issue == Issue.OFFLINE) -> show(Screen.Login, issue = issue)
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
        recheck?.cancel()
        recheck = null
        expiryCheck?.cancel()
        expiryCheck = null
    }

    private fun lockOffline(issue: Issue) {
        interrupt()
        show(Screen.Login, issue = issue)
    }

    private fun show(screen: Screen, busy: Boolean = false, issue: Issue? = null, message: String? = null) {
        val local = ui.value.offline.takeUnless { screen == Screen.Login }
        if (local != null && requireNotNull(cache).remainingMs(local) <= 0) {
            lockOffline(Issue.OFFLINE_EXPIRED)
            return
        }
        if (screen !is Screen.SampleWorkbench && ui.value.screen is Screen.SampleWorkbench) workbench?.close()
        mutableUi.value = ui.value.copy(screen = screen, busy = busy, issue = issue, message = message,
            offline = local)
    }

    private companion object {
        val END_SESSION = setOf(Issue.UNAUTHENTICATED, Issue.FORBIDDEN, Issue.REAUTHENTICATE, Issue.STORAGE,
            Issue.OFFLINE_EXPIRED, Issue.OFFLINE_CLOCK, Issue.CACHE_STORAGE)
    }
}
