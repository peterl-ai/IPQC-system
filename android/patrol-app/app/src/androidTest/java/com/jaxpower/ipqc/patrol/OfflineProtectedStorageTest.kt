package com.jaxpower.ipqc.patrol

import android.database.sqlite.SQLiteConstraintException
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.DETAIL
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.PRIVATE_FIXTURE_TEXT
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.TASK
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.offline.AndroidOfflineClock
import com.jaxpower.ipqc.patrol.offline.CacheChunk
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import java.io.File
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflineProtectedStorageTest {
    @Test fun realKeystoreAndRoomSurviveCloseReopenAndNormalBootClockChange() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            val androidSample = AndroidOfflineClock(device.context).sample()
            assertTrue(androidSample.boot >= 0 && androidSample.elapsedMs > 0)
            device.download()
            assertTrue(device.receiptFile.isFile)
            assertTrue(device.databaseFile.isFile)
            assertEncryptedFiles(device)
            device.database.close()
            assertEncryptedFiles(device)
            device.sample = device.sample.copy(boot = device.sample.boot + 1, elapsedMs = 4_000,
                utcMs = device.sample.utcMs + 45_000)
            TaskCacheDatabase.open(device.context, device.databaseFile).useDatabase { reopened ->
                val cache = device.openCache(reopened)
                val access = requireNotNull(cache.resume())
                assertEquals(IDENTITY, access.identity)
                assertEquals(Instant.parse(NOW).plusSeconds(45), access.apparentNow)
                assertEquals(DETAIL, cache.detail(IDENTITY, LINE, TASK, 1))
                assertEquals(5, device.api.calls.get())
            }
        }
    }

    @Test fun logoutDestroysAdmissionAndCredentialsButIndependentKeyStillOpensArchive() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            assertNotNull(device.vault.read())
            assertFalse(device.sessions.logout(attemptRemote = false))
            assertNull(device.vault.read())
            device.sample = device.sample.copy(boot = device.sample.boot + 1, elapsedMs = 500,
                utcMs = device.sample.utcMs + 10_000)
            val cache = device.openCache()
            assertNull(cache.resume())
            expect(Issue.OFFLINE) { cache.detail(IDENTITY, LINE, TASK, 1) }
            assertEquals(DETAIL, device.snapshots.detail(IDENTITY, TASK))
            assertEquals(5, device.api.calls.get())
        }
    }

    @Test fun alteredReceiptAndAlteredRoomCiphertextBothFailClosed() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val original = device.receiptFile.readBytes()
            device.receiptFile.writeBytes(original.copyOf().apply { this[lastIndex] = (last().toInt() xor 1).toByte() })
            expect(Issue.CACHE_STORAGE) { device.openCache().resume() }
            // Restore the fixture's authenticated receipt to test the separate work store.
            device.receiptFile.writeBytes(original)
            val db = device.database.openHelper.writableDatabase
            db.execSQL("UPDATE cache_chunk SET ciphertext = zeroblob(length(ciphertext)) WHERE slot != 'workspace-v1'")
            val cache = device.openCache()
            assertEquals(IDENTITY, cache.resume()?.identity)
            expect(Issue.CACHE_STORAGE) { cache.lines(IDENTITY) }
            assertTrue(device.rows.count() > 0)
        }
    }

    @Test fun missingWorkKeyNeverRegeneratesOverExistingRowsAndSlotSubstitutionIsRejected() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val sealed = device.workKey.seal("source", byteArrayOf(2))
            assertTrue(runCatching { device.workKey.open("target", sealed) }.isFailure)
            val before = device.rows.count()
            assertTrue(device.workKey.removeKey())
            expect(Issue.CACHE_STORAGE) { device.cache.lines(IDENTITY) }
            assertEquals(before, device.rows.count())
        }
    }

    @Test fun interruptedRealRoomReplacementRollsBackAndUnsupportedSchemaDoesNotEraseRows() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val dao = device.database.entries()
            val slot = "synthetic-transaction"
            val before = device.workKey.seal(slot, byteArrayOf(1))
            val replacement = device.workKey.seal(slot, byteArrayOf(2))
            device.rows.replace(slot, before)
            val duplicate = CacheChunk(slot, 0, replacement)
            try {
                dao.replace(slot, listOf(duplicate, duplicate))
                fail("Duplicate part must roll back the SQLite replacement")
            } catch (_: SQLiteConstraintException) {
                assertArrayEquals(before, device.rows.read(slot))
            }
            val largeSlot = "synthetic-multi-chunk"
            val bytes = ByteArray(800_000) { 7 }
            val sealed = device.workKey.seal(largeSlot, bytes)
            device.rows.replace(largeSlot, sealed)
            assertTrue(dao.read(largeSlot).size > 1)
            assertArrayEquals(bytes, device.workKey.open(largeSlot, requireNotNull(device.rows.read(largeSlot))))

            val originalRows = device.rows.count()
            device.database.openHelper.writableDatabase.version = 2
            device.database.close()

            TaskCacheDatabase.open(device.context, device.databaseFile).useDatabase { unsupported ->
                expect(Issue.CACHE_STORAGE) { device.openCache(unsupported).lines(IDENTITY) }
            }
            // Restore the test-only version marker and prove the rejected open preserved contents.
            android.database.sqlite.SQLiteDatabase.openDatabase(device.databaseFile.path, null,
                android.database.sqlite.SQLiteDatabase.OPEN_READWRITE).use { it.version = 1 }
            TaskCacheDatabase.open(device.context, device.databaseFile).useDatabase { reopened ->
                assertEquals(originalRows, reopened.entries().count())
                assertEquals(DETAIL, device.openCache(reopened).detail(IDENTITY, LINE, TASK, 1))
            }
        }
    }

    private fun assertEncryptedFiles(device: OfflineDeviceFixture) {
        val files = listOf(device.receiptFile, device.databaseFile,
            File(device.databaseFile.path + "-wal"), File(device.databaseFile.path + "-shm"))
        for (file in files.filter(File::exists)) {
            val bytes = file.readBytes().toString(Charsets.ISO_8859_1)
            for (value in listOf(PRIVATE_FIXTURE_TEXT, IDENTITY.userId, IDENTITY.familyId, IDENTITY.environment,
                TASK.id, LINE.name, "Fixture Inspector", "fixture-refresh", "fixture-access")) {
                assertFalse("Private fixture value present at rest", bytes.contains(value))
            }
        }
    }

    private inline fun <T> TaskCacheDatabase.useDatabase(block: (TaskCacheDatabase) -> T): T = try {
        block(this)
    } finally {
        close()
    }

    private suspend fun expect(issue: Issue, action: suspend () -> Any?) {
        try {
            action()
            fail("Expected $issue")
        } catch (e: PatrolFailure) {
            assertEquals(issue, e.issue)
        }
    }
}
