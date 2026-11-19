package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.OfflineAccessReceipt
import com.jaxpower.ipqc.patrol.offline.ClockProgress
import com.jaxpower.ipqc.patrol.offline.ClockSample
import java.time.Instant
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Test

class OfflineClockTest {
    private val receipt = Json.decodeFromString<OfflineAccessReceipt>(Fixture.receipt())
    private val verified = Instant.parse(receipt.verifiedAtUtc)
    private val initial = ClockSample(42, 10_000, verified.toEpochMilli())
    private val anchor = ClockProgress.startingAt(initial, receipt.verifiedAtUtc)
    private val window = 72 * 60 * 60 * 1_000L

    @Test fun sameBootCountsElapsedTimeIncludingSleepAndExpiresAtExactly72Hours() = runBlocking<Unit> {
        val before = anchor.observe(receipt, initial.advance(window - 1))
        assertEquals(verified.plusMillis(window - 1).toString(), before.lastServerUtc)
        expectIssue(Issue.OFFLINE_EXPIRED) { before.observe(receipt, initial.advance(window)) }
        expectIssue(Issue.OFFLINE_EXPIRED) { anchor.observe(receipt, initial.advance(window + 1)) }
    }

    @Test fun sameBootProcessRestartUsesPersistedServerAnchorAndOriginalDeadline() = runBlocking<Unit> {
        val persisted = Json.encodeToString(anchor.observe(receipt, initial.advance(60_000)))
        val reopened = Json.decodeFromString<ClockProgress>(persisted)
        val next = reopened.observe(receipt, initial.advance(window - 1))
        assertEquals(anchor.anchor, next.anchor)
        assertEquals(anchor.anchorServerUtc, next.anchorServerUtc)
        expectIssue(Issue.OFFLINE_EXPIRED) { next.observe(receipt, initial.advance(window)) }
    }

    @Test fun normalRebootWithUtcProgressKeepsTheRemainingWindowAcrossAnotherRestart() = runBlocking<Unit> {
        val previous = anchor.observe(receipt, initial.advance(17_000))
        val reboot = ClockSample(43, 3000, initial.utcMs + 44_000)
        val checked = previous.observe(receipt, reboot)
        assertEquals(verified.plusSeconds(44).toString(), checked.lastServerUtc)
        assertEquals(reboot, checked.anchor)
        val restarted = Json.decodeFromString<ClockProgress>(Json.encodeToString(checked))
        val justBefore = reboot.advance(window - 44_000 - 1)
        assertEquals(verified.plusMillis(window - 1).toString(), restarted.observe(receipt, justBefore).lastServerUtc)
        expectIssue(Issue.OFFLINE_EXPIRED) { restarted.observe(receipt, justBefore.advance(1)) }
    }

    @Test fun firstCheckAfterRebootExpiresAccordingToPersistedAndDeviceUtcProgress() = runBlocking<Unit> {
        val previous = anchor.observe(receipt, initial.advance(60_000))
        expectIssue(Issue.OFFLINE_EXPIRED) {
            previous.observe(receipt, ClockSample(43, 20_000, initial.utcMs + window))
        }
    }

    @Test fun enforcedDeadlineCanBeEarlierThan72HoursAtTheAbsoluteFamilyExpiry() = runBlocking<Unit> {
        val familyExpiry = verified.plusSeconds(90).toString()
        val capped = receipt.copy(offlineAccessUntilUtc = familyExpiry, personalSessionExpiresAtUtc = familyExpiry)
        assertEquals(verified.plusMillis(89_999).toString(), anchor.observe(capped, initial.advance(89_999)).lastServerUtc)
        expectIssue(Issue.OFFLINE_EXPIRED) { anchor.observe(capped, initial.advance(90_000)) }
        expectIssue(Issue.OFFLINE_EXPIRED) {
            anchor.observe(capped, ClockSample(43, 8_000, initial.utcMs + 90_000))
        }
    }

    @Test fun rejectsDetectableWallRollbackForwardJumpAndUnexpectedMonotonicReset() = runBlocking<Unit> {
        val observed = anchor.observe(receipt, initial.advance(22_000))
        val anomalies = listOf(
            initial.advance(23_000).copy(utcMs = observed.last.utcMs - 1),
            initial.advance(23_000).copy(utcMs = observed.last.utcMs + 120_000),
            initial.advance(23_000).copy(elapsedMs = 0),
            initial.advance(23_000).copy(boot = 41),
            ClockSample(43, 10_000, observed.last.utcMs + 5_000),
            ClockSample(43, 50, observed.last.utcMs - 1),
            initial.advance(23_000).copy(boot = -1),
        )
        for (sample in anomalies) expectIssue(Issue.OFFLINE_CLOCK) { observed.observe(receipt, sample) }
    }

    @Test fun rejectsInconsistentPersistedProgressAndDeadlineSubstitution() = runBlocking<Unit> {
        val progressed = anchor.observe(receipt, initial.advance(60_000))
        for (bad in listOf(
            progressed.copy(lastServerUtc = verified.toString()),
            progressed.copy(anchor = initial.copy(elapsedMs = -1)),
            progressed.copy(last = progressed.last.copy(boot = 41)),
            progressed.copy(anchorServerUtc = verified.minusSeconds(1).toString()),
        )) expectIssue(Issue.OFFLINE_CLOCK) { bad.observe(receipt, initial.advance(61_000)) }
        expectIssue(Issue.OFFLINE_CLOCK) {
            anchor.observe(receipt.copy(offlineAccessUntilUtc = verified.plusMillis(window + 1).toString()), initial)
        }
    }

    private fun ClockSample.advance(millis: Long) = copy(elapsedMs = elapsedMs + millis, utcMs = utcMs + millis)
}
