package com.jaxpower.ipqc.patrol.offline

import android.content.Context
import android.os.SystemClock
import android.provider.Settings
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.OfflineAccessReceipt
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.utc
import java.time.Instant
import kotlinx.serialization.Serializable

@Serializable
internal data class ClockSample(val boot: Int, val elapsedMs: Long, val utcMs: Long) {
    fun check() {
        if (boot < 0 || elapsedMs < 0 || utcMs <= 0) invalidClock()
    }
}

internal fun interface OfflineClock {
    fun sample(): ClockSample
    fun elapsedRealtimeMs(): Long = sample().elapsedMs
}

internal class AndroidOfflineClock(private val context: Context) : OfflineClock {
    override fun elapsedRealtimeMs(): Long = SystemClock.elapsedRealtime()

    override fun sample(): ClockSample {
        val boot = Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1)
        val elapsed = elapsedRealtimeMs()
        return ClockSample(boot, elapsed, System.currentTimeMillis()).also { it.check() }
    }
}

@Serializable
internal data class ClockProgress(
    val anchor: ClockSample,
    val anchorServerUtc: String,
    val last: ClockSample,
    val lastServerUtc: String,
) {
    fun observe(receipt: OfflineAccessReceipt, sample: ClockSample): ClockProgress {
        val verified = utc(receipt.verifiedAtUtc)
        val until = utc(receipt.offlineAccessUntilUtc)
        val family = utc(receipt.personalSessionExpiresAtUtc)
        val anchorTime = utc(anchorServerUtc)
        val lastTime = utc(lastServerUtc)
        anchor.check()
        last.check()
        sample.check()
        if (receipt.version != 1 || !verified.isBefore(family) ||
            until != minOf(verified.plusSeconds(WINDOW_SECONDS), family) ||
            anchorTime.isBefore(verified) || last.boot != anchor.boot ||
            lastTime != sameBoot(anchor, anchorTime, last) ||
            sample.boot < last.boot || sample.utcMs < last.utcMs) invalidClock()

        val next = if (sample.boot == last.boot) {
            sameBootElapsed(last, sample)
            copy(last = sample, lastServerUtc = sameBoot(anchor, anchorTime, sample).toString())
        } else {
            val wallProgress = sample.utcMs - last.utcMs
            // The UTC interval since the last observation must cover the new boot's uptime.
            if (sample.elapsedMs - wallProgress > CLOCK_TOLERANCE_MS) invalidClock()
            val apparentNow = lastTime.plusMillis(maxOf(wallProgress, sample.elapsedMs))
            startingAt(sample, apparentNow.toString())
        }
        if (!utc(next.lastServerUtc).isBefore(until)) throw PatrolFailure(Issue.OFFLINE_EXPIRED)
        return next
    }

    companion object {
        const val WINDOW_SECONDS = 72 * 3600L
        private const val CLOCK_TOLERANCE_MS = 2_000L

        fun startingAt(sample: ClockSample, serverUtc: String) = ClockProgress(sample, serverUtc, sample, serverUtc)

        private fun sameBoot(anchor: ClockSample, server: Instant, sample: ClockSample): Instant =
            server.plusMillis(sameBootElapsed(anchor, sample))

        private fun sameBootElapsed(anchor: ClockSample, sample: ClockSample): Long {
            val elapsed = sample.elapsedMs - anchor.elapsedMs
            val wall = sample.utcMs - anchor.utcMs
            if (elapsed < 0 || wall < 0 || wall - elapsed !in -CLOCK_TOLERANCE_MS..CLOCK_TOLERANCE_MS)
                invalidClock()
            return elapsed
        }
    }
}

private fun invalidClock(): Nothing = throw PatrolFailure(Issue.OFFLINE_CLOCK)
