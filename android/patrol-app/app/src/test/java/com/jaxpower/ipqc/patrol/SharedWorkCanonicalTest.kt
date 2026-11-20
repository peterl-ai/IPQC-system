package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class SharedWorkCanonicalTest {
    private val task = WorkTask("synthetic", testId(1), testId(2), 2)
    private val source = WorkSource(task, testId(7), testId(8), testId(99), Fixture.NOW,
        "2026-11-22T14:00:00Z", Fixture.FAMILY_EXPIRY, 1)
    private val input = SampleOperation(testId(3), WorkAttempt(testId(4), testId(5)), testId(6), testId(7), 0,
        SampleKind.CaptureSample, SamplePayload(null, "1.2300", "汉字"), emptyList())

    @Test fun matchesBackendFrozenOperationAndBatchVectors() {
        val canonical = DraftOperationCanonical.operation(source, DraftOperationCanonical.normalize(input), 1)
        val hash = workHash(canonical)
        // Independently pinned to SharedDraftCanonicalTests.OP11_Frozen_V1_byte_vector_decimal_null_and_Unicode.
        assertEquals("19E779821A27A76CD900FCF8A06F1C70E9CEE2CEDFF84A5B439BF96760A047DB", hash)
        assertEquals("29E81D9F21E2F3D057B0BC3371F0FAAEF65C07B5B38B2921B0CAB994B391CB3E",
            DraftOperationCanonical.batch(task, testId(9), 0, listOf(OperationReference(testId(3), hash)), emptyList()))
        assertFalse(canonical.contains(source.originPersonalSessionId))
    }

    @Test fun exactDecimalUnicodeAndNullFormsStayStable() {
        fun encoded(number: String, barcode: String?) = DraftOperationCanonical.operation(source,
            DraftOperationCanonical.normalize(input.copy(data = SamplePayload(null, number, barcode))), 1)

        assertEquals(encoded("0", null), encoded("-0.000", null))
        assertEquals(encoded("0.00001", null), encoded("1E-05", null))
        assertTrue(encoded("0.00001", null).contains("\"value\":\"1E-05\""))
        assertTrue(encoded("2", "<+&\"é😀").contains("\\u003C\\u002B\\u0026\\u0022\\u00E9\\uD83D\\uDE00"))
        assertNotEquals(encoded("1", null), encoded("1", ""))
        assertNotEquals(encoded("1", "é"), encoded("1", "e\u0301"))
        for (unsupported in listOf("1e9999", "0.00000000000000000000000000001", "79228162514264337593543950336")) {
            assertThrows(IllegalArgumentException::class.java) { encoded(unsupported, null) }
        }
    }

    @Test fun conflictAndQuarantineKeepImmutableEnvelopesAndDisableAnotherAttempt() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val current = f.preparedSource()
            val conflict = f.store.enqueue(current, f.capture())
            val quarantine = f.store.enqueue(current, f.capture(11, sample = testId(12)))
            val unbatched = f.store.enqueue(current, f.capture(12, sample = testId(13)))
            val first = f.store.prepareBatch(current, testId(70), listOf(conflict.envelope.operation.operationId))
            val second = f.store.prepareBatch(current, testId(71), listOf(quarantine.envelope.operation.operationId))
            f.store.startAttempt(current, first.batchId)
            val latest = f.access(version = 8)
            f.store.recordRejection(latest, first.batchId, WorkConflict.DraftVersionConflict)
            f.store.recordRejection(latest, second.batchId, WorkConflict.SourceSecurityRevoked)
            f.store.quarantineUnbatched(latest, unbatched.envelope.operation.operationId, WorkConflict.SourceSecurityRevoked)
            val reopened = f.reopen()
            val originals = reopened.operations(latest)
            assertEquals(listOf(WorkState.Conflict, WorkState.Quarantined, WorkState.Quarantined), originals.map { it.state })
            assertEquals(listOf(conflict, quarantine, unbatched).map { it.envelope.canonicalPayload },
                originals.map { it.envelope.canonicalPayload })
            assertEquals(8L, originals[0].conflict?.observedDraftVersion)
            assertTrue(originals.all { it.envelope.operation.baseDraftVersion == 7L })
            expectWork(WorkProblem.InvalidState) { reopened.startAttempt(latest, first.batchId) }
            expectWork(WorkProblem.InvalidState) { reopened.startAttempt(latest, second.batchId) }
            assertEquals(2, server.web.requestCount)
        }
    }
}
