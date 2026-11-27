package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.*
import org.junit.Test

class BasicInformationFoundationTest {
    private val task = WorkTask("synthetic-header-vector", id(1), id(2), 2)

    @Test fun matchesBackendOperationAndBatchV1GoldenVectors() {
        val op = operation(3, BasicInformationField.Customer, "é", 7)
        // BasicInformationIdentityTests.Fixed_protocol_vectors_do_not_depend_on_transport_proof_or_normalize_Unicode.
        val expected = """{"domain":"basic-information-field","v":1,"environment":"synthetic-header-vector","task":"00000000-0000-0000-0000-000000000001","line":"00000000-0000-0000-0000-000000000002","workflow":2,"operation":"00000000-0000-0000-0000-000000000003","kind":"CaptureBasicInformationField","field":"Customer","value":"\u00E9","source":"00000000-0000-0000-0000-000000000004","contributorAssertion":"00000000-0000-0000-0000-000000000005","base":7,"local":9,"predecessor":null}"""
        val bytes = BasicInformationCanonical.operation(task, op, 9)
        assertEquals(expected, bytes)
        val hash = "0822F12248A4D7059744C1E43360B04189EB2EBEDD3E2ED733CC6F0D9596B2E4"
        assertEquals(hash, workHash(bytes))
        assertEquals("00C7A10F825F83E18B01E776AAB8C9D9CF08EB7164CD92FF6502A532B6CDBEE0",
            BasicInformationCanonical.fingerprint(task, id(6), 7, OperationReference(op.operationId, hash)))
        assertNotEquals(BasicInformationCanonical.fingerprint(task, id(6), 7, OperationReference(op.operationId, hash)),
            DraftOperationCanonical.batch(task, id(6), 7, listOf(OperationReference(op.operationId, hash)), emptyList()))
        assertNotEquals(hash, workHash(BasicInformationCanonical.operation(task, op.copy(value = "e\u0301"), 9)))
        assertEquals(3, listOf(null, "", " ").map { workHash(BasicInformationCanonical.operation(task, op.copy(value = it), 9)) }
            .distinct().size)
        val prior = OperationReference(id(8), "A".repeat(64))
        val correction = operation(3, BasicInformationField.Customer, null, 7, prior)
        assertTrue(BasicInformationCanonical.operation(task, correction, 9)
            .endsWith(""""predecessor":{"operation":"${prior.operationId}","hash":"${prior.contentHash}"}}"""))
    }

    @Test fun valuesRetainExactScalarsAndBackendWhitespaceClassification() {
        for (text in listOf(null, "", "  ", "é", "e\u0301", "x".repeat(200), "📌".repeat(200), "\u00A0")) {
            assertTrue(validBasicInformationValue(text))
            val fact = decodeRead(receipt(operation(3, BasicInformationField.Customer, text, 7))).basis
                .field(BasicInformationField.Customer)
            assertEquals(text, fact.value)
        }
        for (text in listOf("x".repeat(201), "📌".repeat(201), "\u0000", "\n", "\u0085", "\uD800", "\uDC00")) {
            assertFalse(validBasicInformationValue(text))
        }
        val none = fixture(7).decodeBasicInformation(task, BasicInformationQuery(expectedDraftVersion = 7))
        assertEquals(BasicInformationPresence.Unrecorded, none.basis.field(BasicInformationField.Customer).presence)
        val states = listOf(null to BasicInformationPresence.ExplicitNull, "" to BasicInformationPresence.Empty,
            " \u00A0" to BasicInformationPresence.Whitespace, " e\u0301 " to BasicInformationPresence.Provided)
        for ((text, state) in states) {
            assertEquals(state, decodeRead(receipt(operation(3, BasicInformationField.Customer, text, 7))).basis
                .field(BasicInformationField.Customer).presence)
        }
    }

    @Test fun preservesCurrentProvenanceAndValidatesConsistentHistoryPages() {
        val old = receipt(operation(11, BasicInformationField.Customer, "Original", 5))
        val other = receipt(operation(12, BasicInformationField.Production, " ", 6))
        val corrected = receipt(operation(13, BasicInformationField.Customer, null, 7, old.reference()))
        val current = listOf(corrected, other)
        val first = fixture(8, current, listOf(corrected, other), 7)
            .decodeBasicInformation(task, BasicInformationQuery(2, expectedDraftVersion = 8))
        assertEquals(7L, first.historyBeforeDraftVersion)
        assertEquals(corrected, first.basis.field(BasicInformationField.Customer).accepted)
        assertEquals(other, first.basis.field(BasicInformationField.Production).accepted)
        assertNotEquals(corrected.operation.contributorUserId, corrected.actorUserId)
        val older = fixture(8, current, listOf(old))
            .decodeBasicInformation(task, BasicInformationQuery(2, beforeDraftVersion = 7, expectedDraftVersion = 8))
        assertEquals(first.basis, older.basis)
        assertEquals(listOf(old), older.history)
        invalid(fixture(8, current, listOf(corrected, other), 8), BasicInformationQuery(2))
        invalid(fixture(8, current, listOf(other, corrected, old)))
        invalid(fixture(8, current, listOf(corrected, other)))
        val wrong = receipt(operation(13, BasicInformationField.Customer, null, 7, other.reference()))
        invalid(fixture(8, listOf(wrong, other), listOf(wrong, other, old)))
    }

    @Test fun rejectsContradictoryFieldsScopeAuthorityCountsAndWireTypes() {
        val remote = receipt(operation(3, BasicInformationField.Customer, "Synthetic", 7))
        val base = fixture(8, listOf(remote), listOf(remote))
        val fields = base.getValue("fields").jsonArray
        val customer = fields[0].jsonObject
        for (bad in listOf(
            base.replacing("taskId", id(32)), base.replacing("lineId", id(33)),
            base.replacing("workflowVersion", 1), base.replacing("draftVersion", "8"), base.replacing("availability", "Undefined"),
            base.replacing("canExecute", true), base.replacing("canSubmit", "false"),
            base.replacing("acceptsFormalInspection", true), base.replacing("acceptedFieldCount", 2),
            base.replacing("nonblankFieldCount", 0), JsonObject(base - "historyBeforeDraftVersion"),
            base.replacing("fields", kotlinx.serialization.json.JsonArray(listOf(fields[0], fields[0], fields[2]))),
            base.replacing("fields", kotlinx.serialization.json.JsonArray(listOf(customer.replacing("field", "MachineCode"), fields[1], fields[2]))),
            base.replacing("fields", kotlinx.serialization.json.JsonArray(listOf(customer.replacing("valueState", "ExplicitNull"), fields[1], fields[2]))),
            base.replacing("fields", kotlinx.serialization.json.JsonArray(listOf(JsonObject(customer - "value"), fields[1], fields[2]))),
            base.replacing("fields", kotlinx.serialization.json.JsonArray(listOf(customer.replacing("hasAcceptedFact", false), fields[1], fields[2]))),
            base.replacing("history", kotlinx.serialization.json.JsonArray(listOf(receiptJson(remote).replacing("environmentId", "foreign")))),
            base.replacing("history", kotlinx.serialization.json.JsonArray(listOf(receiptJson(remote).replacing("contentHash", "F".repeat(64))))),
            base.replacing("history", kotlinx.serialization.json.JsonArray(listOf(receiptJson(remote).replacing("value", "x".repeat(201))))),
        )) invalid(bad)
        invalid(base, BasicInformationQuery(expectedDraftVersion = 7))
    }

    @Test fun performsOnlyAuthenticatedGetWithExactQueryAndNoPinnedFallback() = runBlocking<Unit> {
        TestServer().use { server ->
            val path = "/api/line-patrol/tasks/" + task.taskId + "/shared-draft/basic-information"
            val suffix = "?expectedTaskLineId=" + task.lineId + "&expectedDraftVersion=7&beforeDraftVersion=6&limit=2"
            val query = BasicInformationQuery(2, beforeDraftVersion = 6, expectedDraftVersion = 7)
            server.enqueue(fixture(7).toString())
            assertEquals(7L, server.workApi.readBasicInformation("Bearer synthetic-header-read", task, query).basis.draftVersion)
            assertEquals(0L, server.request(path + suffix, "GET", "synthetic-header-read").bodySize)

            server.enqueue("""{"status":409,"code":"BasicInformationVersionConflict"}""", 409)
            try {
                server.workApi.readBasicInformation("Bearer synthetic-header-read", task, query)
                fail("Expected pinned conflict")
            } catch (_: BasicInformationVersionConflict) {
                server.request(path + suffix, "GET", "synthetic-header-read")
            }
            server.enqueue(fixture(7).toString().replaceFirst("\"taskId\":", "\"taskId\":null,\"ta\\u0073kId\":"))
            expectIssue(Issue.RESPONSE) { server.workApi.readBasicInformation("Bearer synthetic-header-read", task, query) }
            server.request(path + suffix, "GET", "synthetic-header-read")
            server.enqueue(" ".repeat(513 * 1024))
            expectIssue(Issue.RESPONSE) { server.workApi.readBasicInformation("Bearer synthetic-header-read", task, query) }
            server.request(path + suffix, "GET", "synthetic-header-read")
            assertEquals(4, server.web.requestCount)
        }
    }

    @Test fun keepsPreviousCatalogVersionAndV6OutOfOlderReceiptDecoders() {
        val saved = Json.decodeFromString<WorkCatalog>("""{"observedItemRemovalVersion":8,"localEditVersion":2}""")
        assertEquals(8L, saved.observedTaskDraftVersion)
        assertEquals(2L, saved.localEditVersion)
        assertTrue(saved.basicInformationIds.isEmpty())
        val headerBatch = Json.parseToJsonElement("""{"receiptVersion":6,"operations":[],"headerOperations":[]}""").jsonObject
        assertThrows(Exception::class.java) { headerBatch.decodeBatchReceipt() }
        assertThrows(Exception::class.java) { headerBatch.decodeItemRemovalBatchReceipt() }
    }

    private fun decodeRead(r: BasicInformationReceipt) =
        fixture(r.draftVersion, listOf(r), listOf(r)).decodeBasicInformation(task, BasicInformationQuery())

    private fun invalid(base: JsonObject, query: BasicInformationQuery = BasicInformationQuery()) {
        val failure = assertThrows(PatrolFailure::class.java) { base.decodeBasicInformation(task, query) }
        assertEquals(Issue.RESPONSE, failure.issue)
    }

    private fun operation(n: Int, field: BasicInformationField, value: String?, base: Long,
                          predecessor: OperationReference? = null) = BasicInformationOperation(
        id(n), if (predecessor == null) BasicInformationKind.CaptureBasicInformationField
        else BasicInformationKind.CorrectBasicInformationField, field, value, id(4), id(5), base, predecessor)

    private fun receipt(op: BasicInformationOperation) = BasicInformationReceipt(task, op,
        id(20 + op.baseDraftVersion.toInt()), workHash(BasicInformationCanonical.operation(task, op, 9)),
        id(41), id(42), 9, Fixture.NOW, op.baseDraftVersion + 1)

    private fun fixture(version: Long, current: List<BasicInformationReceipt> = emptyList(),
                        history: List<BasicInformationReceipt> = emptyList(), cursor: Long? = null) = buildJsonObject {
        val facts = BasicInformationField.entries.map { field ->
            BasicInformationFact(field, current.singleOrNull { it.operation.field == field })
        }
        put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", task.workflowVersion)
        put("draftVersion", version); put("availability", if (version == 0L) "FoundationOnly" else "DraftFoundation")
        put("values", buildJsonObject { facts.forEach { put(it.field.name.replaceFirstChar(Char::lowercaseChar), it.value) } })
        put("fields", kotlinx.serialization.json.JsonArray(facts.map { fact ->
            buildJsonObject {
                put("field", fact.field.name); put("value", fact.value); put("valueState", fact.presence.name)
                put("hasAcceptedFact", fact.accepted != null); put("lastOperation", fact.accepted?.let(::receiptJson) ?: JsonNull)
                put("isRequired", false); put("blocksSubmit", false)
            }
        }))
        put("acceptedFieldCount", current.size)
        put("nonblankFieldCount", facts.count { it.presence == BasicInformationPresence.Provided })
        put("history", kotlinx.serialization.json.JsonArray(history.map(::receiptJson)))
        put("historyBeforeDraftVersion", cursor)
        put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
        put("completeness", "OptionalHeaderFactsOnly"); put("observation", "ReadOnlyPointInTime")
    }

    private fun receiptJson(r: BasicInformationReceipt) = buildJsonObject {
        val op = r.operation
        put("taskId", r.task.taskId); put("operationId", op.operationId); put("batchId", r.batchId)
        put("canonicalizationVersion", 1); put("contentHash", r.contentHash); put("kind", op.kind.name); put("field", op.field.name)
        put("value", op.value); put("sourceWorkContextId", op.sourceWorkContextId); put("contributorUserId", op.contributorUserId)
        put("actorUserId", r.actorUserId); put("actorPersonalSessionId", r.actorPersonalSessionId)
        put("environmentId", r.task.environmentId); put("lineId", r.task.lineId); put("workflowVersion", r.task.workflowVersion)
        put("baseDraftVersion", op.baseDraftVersion); put("originLocalEditVersion", r.originLocalEditVersion)
        put("acceptedAtUtc", r.acceptedAtUtc); put("draftVersion", r.draftVersion)
        put("predecessor", op.predecessor?.let { p ->
            buildJsonObject { put("operationId", p.operationId); put("contentHash", p.contentHash) }
        } ?: JsonNull)
    }

    private fun JsonObject.replacing(key: String, value: kotlinx.serialization.json.JsonElement) = JsonObject(this + (key to value))
    private fun JsonObject.replacing(key: String, value: String) = replacing(key, JsonPrimitive(value))
    private fun JsonObject.replacing(key: String, value: Number) = replacing(key, JsonPrimitive(value))
    private fun JsonObject.replacing(key: String, value: Boolean) = replacing(key, JsonPrimitive(value))
    private fun id(n: Int) = "00000000-0000-0000-0000-" + n.toString().padStart(12, '0')
}
