package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import okhttp3.mockwebserver.MockResponse
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

    @Test fun v6AcceptsExactReplayAndDistinguishesOperationOnlyResults() {
        val op = operation(3, BasicInformationField.Customer, null, 7, OperationReference(id(8), "A".repeat(64)))
        val operation = receipt(op)
        val full = uploadResult(operation)
        for (replay in listOf(false, true)) {
            val parsed = full.replacing("replayOnly", replay).decodeBasicInformationUpload(task)
            assertEquals(replay, parsed.replayOnly)
            assertEquals(operation, parsed.operation)
            assertEquals(operation.actorPersonalSessionId, parsed.receipt?.operation?.actorPersonalSessionId)
            assertEquals(op.predecessor, parsed.receipt?.operation?.operation?.predecessor)
            assertNull(parsed.operation.operation.value)
        }
        val individual = full.replacing("replayOnly", true).replacing("batchReceipt", JsonNull)
            .decodeBasicInformationUpload(task)
        assertNull(individual.receipt)
        assertEquals(operation, individual.operation)
        assertThrows(PatrolFailure::class.java) { full.replacing("batchReceipt", JsonNull).decodeBasicInformationUpload(task) }
    }

    @Test fun v6RejectsIncompatibleMembersTamperedPinsAndContradictoryOuterResponse() {
        val operation = receipt(operation(3, BasicInformationField.Customer, " ", 7))
        val full = uploadResult(operation)
        val batch = full.getValue("batchReceipt").jsonObject
        val individual = full.getValue("operation").jsonObject
        for (bad in listOf(
            full.replacing("canSubmit", true), full.replacing("replayOnly", "true"),
            full.replacing("operation", individual.replacing("value", JsonNull)),
            full.replacing("operation", individual.replacing("field", "ProductMo")),
            full.replacing("operation", individual.replacing("originLocalEditVersion", 10)),
            full.replacing("operation", individual.replacing("predecessor",
                buildJsonObject { put("operationId", id(8)); put("contentHash", "A".repeat(64)) })),
            full.replacing("operation", individual.replacing("operationId", id(90))),
            full.replacing("operation", individual.replacing("actorPersonalSessionId", "invalid")),
            full.replacing("operation", individual.replacing("environmentId", "other")),
            full.replacing("operation", individual.replacing("lineId", id(94))),
            full.replacing("operation", individual.replacing("value", "x".repeat(201))),
            full.replacing("operation", individual.replacing("acceptedAtUtc", "broken")),
        )) assertThrows(PatrolFailure::class.java) { bad.decodeBasicInformationUpload(task) }
        for (bad in listOf(
            batch.replacing("receiptVersion", 2), batch.replacing("receiptVersion", "6"),
            batch.replacing("taskId", id(30)), batch.replacing("batchId", id(31)),
            batch.replacing("draftVersion", 9), batch.replacing("previousDraftVersion", 6),
            batch.replacing("uploadedByUserId", id(40)), batch.replacing("uploadedPersonalSessionId", id(41)),
            batch.replacing("acceptedAtUtc", "2026-11-27T08:59:59Z"), batch.replacing("requestFingerprint", "A".repeat(64)),
            batch.replacing("operations", JsonArray(listOf(individual))),
            batch.replacing("itemOperations", JsonArray(emptyList())),
            batch.replacing("headerOperations", JsonArray(listOf(individual, individual))),
        )) assertThrows(PatrolFailure::class.java) { bad.decodeBasicInformationBatch(task) }
    }

    @Test fun headerPostEncodesOneOperationUsesExactBearerAndNeverFollowsOrRetries() = runBlocking<Unit> {
        TestServer().use { server ->
            val op = operation(3, BasicInformationField.Customer, null, 7, OperationReference(id(8), "A".repeat(64)))
            val owner = SessionIdentity(op.contributorUserId, id(13), task.environmentId)
            val batchId = id(6)
            val operation = receipt(op).copy(batchId = batchId)
            val candidate = candidate(op, owner, batchId, operation.originLocalEditVersion)
            server.enqueue(uploadResult(operation).toString())
            val sent = server.workApi.sendBasicInformationBatch("Bearer synthetic-B", task, candidate.encodeBatch(),
                GuardedUploadExchange { true })
            assertTrue(sent is UploadExchange.Success)
            val path = "/api/line-patrol/tasks/${task.taskId}/basic-information-batches"
            val request = server.request(path, "POST", "synthetic-B")
            val body = Json.parseToJsonElement(request.body.readUtf8()).jsonObject
            val item = body.getValue("operation").jsonObject
            assertEquals(JsonPrimitive(op.contributorUserId), item["contributorUserIdAssertion"])
            assertEquals(JsonPrimitive(op.sourceWorkContextId), item["sourceWorkContextId"])
            assertEquals(JsonNull, item["value"])
            assertEquals(JsonPrimitive(op.predecessor?.operationId), item.getValue("predecessor").jsonObject["operationId"])
            assertFalse("operations" in body)
            assertEquals(JsonPrimitive(7), body["expectedDraftVersion"])
            assertEquals(JsonPrimitive(1), item["canonicalizationVersion"])
            for (status in listOf(302, 307, 503, 408)) {
                server.web.enqueue(MockResponse().setResponseCode(status)
                    .setHeader("Location", server.origin.resolve(path).toString()).setHeader("Retry-After", "0").setBody("{}"))
                assertTrue(server.workApi.sendBasicInformationBatch("Bearer synthetic-B", task, candidate.encodeBatch(),
                    GuardedUploadExchange { true }) is UploadExchange.Unknown)
                server.request(path, "POST", "synthetic-B")
            }
            assertEquals(5, server.web.requestCount)
            assertTrue(server.workApi.sendBasicInformationBatch("Bearer synthetic-B", task, candidate.encodeBatch(),
                GuardedUploadExchange { false }) is UploadExchange.Unknown)
            assertEquals(5, server.web.requestCount)
        }
    }

    @Test fun headerUsesBothExactReceiptRoutesAndPreservesUncertaintyForMissingOrInvalidResults() = runBlocking<Unit> {
        TestServer().use { server ->
            val operation = receipt(operation(3, BasicInformationField.Customer, "Synthetic", 7))
            val version6 = uploadResult(operation).getValue("batchReceipt").jsonObject
            val base = "/api/line-patrol/tasks/${task.taskId}/"
            val batchTarget = ReceiptTarget(ReceiptLookup(ReceiptKind.Batch, operation.batchId), task,
                id(11), operation.batchId, version6.getValue("requestFingerprint").toString().trim('"'))
            val opTarget = batchTarget.copy(lookup = ReceiptLookup(ReceiptKind.Operation, operation.operation.operationId))
            server.enqueue(version6.toString())
            val full = server.workApi.readBasicInformationReceipt(batchTarget, "Bearer synthetic-B")
            assertEquals(operation, (full as BasicInformationReceiptReply.Batch).receipt.operation)
            server.request(base + "operation-batches/${operation.batchId}/receipt", "GET", "synthetic-B")
            server.enqueue(receiptJson(operation).toString())
            val exact = server.workApi.readBasicInformationReceipt(opTarget, "Bearer synthetic-B")
            assertEquals(operation, (exact as BasicInformationReceiptReply.Operation).receipt)
            val path = base + "basic-information-operations/${operation.operation.operationId}/receipt"
            server.request(path, "GET", "synthetic-B")
            server.enqueue("{}", 404)
            assertEquals(BasicInformationReceiptReply.Missing, server.workApi.readBasicInformationReceipt(opTarget, "Bearer synthetic-B"))
            server.request(path, "GET", "synthetic-B")
            for (bad in listOf("{\"taskId\":1,\"taskId\":2}", " ".repeat(513 * 1024), "{", version6.toString())) {
                server.enqueue(bad)
                assertEquals(BasicInformationReceiptReply.Unavailable(Issue.RESPONSE),
                    server.workApi.readBasicInformationReceipt(opTarget, "Bearer synthetic-B"))
                server.request(path, "GET", "synthetic-B")
            }
            assertEquals(7, server.web.requestCount)
        }
    }

    @Test fun onlyDocumentedDefinitiveHeaderCodesAreClassified() = runBlocking<Unit> {
        TestServer().use { server ->
            val op = operation(3, BasicInformationField.Customer, "", 7)
            val owner = SessionIdentity(op.contributorUserId, id(13), task.environmentId)
            val request = candidate(op, owner, id(6), 9).encodeBatch()
            val cases = listOf(
                Triple(409, "DraftVersionConflict", WorkConflict.DraftVersionConflict),
                Triple(409, "BasicInformationPredecessorConflict", WorkConflict.TaskScopeConflict),
                Triple(400, "InvalidBasicInformationBatch", WorkConflict.BatchInputRejected),
                Triple(409, "BatchIdentityConflict", null), Triple(409, "SourceSecurityRevoked", null),
                Triple(400, "UnhandledError", null), Triple(429, "DraftVersionConflict", null),
            )
            val path = "/api/line-patrol/tasks/${task.taskId}/basic-information-batches"
            for ((status, code, expected) in cases) {
                server.web.enqueue(MockResponse().setResponseCode(status).setHeader("Content-Type", "application/problem+json")
                    .setBody("""{"status":$status,"title":"$code","code":"$code"}"""))
                val result = server.workApi.sendBasicInformationBatch("Bearer synthetic-B", task, request, GuardedUploadExchange { true })
                if (expected == null) assertTrue(result is UploadExchange.Unknown)
                else assertEquals(expected, (result as UploadExchange.Rejected).rejection.reason)
                server.request(path, "POST", "synthetic-B")
            }
            for ((status, issue) in listOf(401 to Issue.UNAUTHENTICATED, 403 to Issue.FORBIDDEN)) {
                server.enqueue("{}", status)
                expectIssue(issue) {
                    server.workApi.sendBasicInformationBatch("Bearer synthetic-B", task, request, GuardedUploadExchange { true })
                }
                server.request(path, "POST", "synthetic-B")
            }
        }
    }

    private fun candidate(op: BasicInformationOperation, owner: SessionIdentity, batchId: String, local: Long):
        BasicInformationUploadCandidate {
        val source = WorkSource(task, op.sourceWorkContextId, owner.userId, owner.familyId, Fixture.NOW,
            "2026-11-19T14:01:00Z", Fixture.FAMILY_EXPIRY, 1)
        val payload = BasicInformationCanonical.operation(task, op, local)
        val original = BasicInformationEnvelope(id(11), BasicInformationCommand(task, owner, op), source,
            local, CANONICAL_VERSION, payload, workHash(payload), OfflineCaptureEvidence(Fixture.NOW, source.captureUntilUtc))
        val batch = BasicInformationBatch.seal(original, batchId)
        return BasicInformationUploadCandidate(ReceiptTarget(ReceiptLookup(ReceiptKind.Batch, batchId), task,
            original.workspaceId, batchId, batch.fingerprint), original, BasicInformationTransport(batch),
            StoredWorkSource(source, "A".repeat(43)))
    }

    private fun uploadResult(operation: BasicInformationReceipt) = buildJsonObject {
        val member = receiptJson(operation)
        put("batchReceipt", buildJsonObject {
            put("receiptVersion", 6); put("taskId", task.taskId); put("batchId", operation.batchId)
            put("requestFingerprint", BasicInformationCanonical.fingerprint(task, operation.batchId,
                operation.operation.baseDraftVersion, operation.reference()))
            put("uploadedByUserId", operation.actorUserId); put("uploadedPersonalSessionId", operation.actorPersonalSessionId)
            put("acceptedAtUtc", operation.acceptedAtUtc); put("previousDraftVersion", operation.operation.baseDraftVersion)
            put("draftVersion", operation.draftVersion); put("operations", JsonArray(emptyList()))
            put("headerOperations", JsonArray(listOf(member)))
        })
        put("operation", member); put("replayOnly", false)
        put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
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
