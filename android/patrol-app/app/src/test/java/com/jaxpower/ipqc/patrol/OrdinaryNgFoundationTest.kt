package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.work.*
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.*
import okhttp3.mockwebserver.MockResponse
import org.junit.Assert.*
import org.junit.Test
import retrofit2.http.POST

class OrdinaryNgFoundationTest {
    private val task = WorkTask("synthetic-ordinary-ng", id(1), id(2), 2)
    private val attempt = WorkAttempt(id(3), id(4))
    private val other = WorkAttempt(id(5), id(6))
    private val source = id(7)
    private val contributor = id(8)
    private val sampleRef = OperationReference(id(9), "A".repeat(64))

    @Test fun matchesBackendGeneratedSetAndCorrectV1Vectors() {
        val resource = requireNotNull(javaClass.getResourceAsStream("/ordinary-ng-v1-vectors.json"))
        val vectors = resource.bufferedReader().use { Json.parseToJsonElement(it.readText()).jsonArray }
        assertEquals(2, vectors.size)
        val set = OrdinaryNgOperation(id(10), OrdinaryNgKind.SetOrdinaryNgDetails, attempt, source, contributor,
            7, OrdinaryNgText(null, "", "  "), null)
        val prior = OperationReference(id(11), "A".repeat(64))
        val correct = set.copy(operationId = id(12), kind = OrdinaryNgKind.CorrectOrdinaryNgDetails,
            text = OrdinaryNgText("é", "e\u0301", "📌 <&\""), predecessor = prior)
        for ((index, operation) in listOf(set, correct).withIndex()) {
            val vector = vectors[index].jsonObject
            val canonical = OrdinaryNgCanonical.operation(task, operation, 9)
            assertEquals(vector.getValue("operation").jsonPrimitive.content, canonical)
            val hash = workHash(canonical)
            assertEquals(vector.getValue("hash").jsonPrimitive.content, hash)
            assertEquals(vector.getValue("fingerprint").jsonPrimitive.content,
                OrdinaryNgCanonical.fingerprint(task, id(13), 7, OperationReference(operation.operationId, hash)))
            assertFalse(canonical.contains("acceptedNgBasis"))
        }
    }

    @Test fun retainsNullableAndExactUtf16TextAndRejectsControlsOrMalformedPairs() {
        val values = listOf(null, "", " \u00a0 ", "é", "e\u0301", "x".repeat(2_000), "📌".repeat(1_000), "z".repeat(1_998) + "📌")
        for (text in values) {
            assertTrue(OrdinaryNgText(text, text, text).isValid())
            val op = receipt(value = OrdinaryNgText(text, text, text))
            assertEquals(op, decode(page(latest = op, history = listOf(op))).items[0].latest?.receipt)
        }
        assertEquals(values.size, values.map { receipt(value = OrdinaryNgText(it, null, null)).contentHash }.distinct().size)
        for (text in listOf("x".repeat(2_001), "📌".repeat(1_000) + "x", "\u0000", "\r", "\u007F",
            "\u0085", "\uD800", "\uDC00", "\uD800A", "A\uDC00")) {
            assertFalse(validOrdinaryNgText(text))
        }
    }

    @Test fun setAndCorrectionBindTheExactSameAttemptPredecessor() {
        val set = receipt()
        val correction = receipt(version = 7, operationId = id(22), prior = set.reference())
        val result = decode(page(latest = correction, history = listOf(correction, set)))
        assertEquals(correction, result.items.first().latest?.receipt)
        assertFalse(result.history.last().currentlyApplicable)
        val wrong = receipt(version = 7, operationId = id(22), prior = OperationReference(set.operation.operationId, "F".repeat(64)))
        malformed(page(latest = wrong, history = listOf(wrong, set)))
        val invalid = receiptJson(set).replace("kind", JsonPrimitive(OrdinaryNgKind.CorrectOrdinaryNgDetails.name))
        malformed(page(latest = set).editItem(0) { replace("latest", observation(set).replace("receipt", invalid)) })
        assertFalse(set.operation.copy(predecessor = set.reference()).isValid())
    }

    @Test fun itemPagesAndHistoryCursorsAreIndependentAndPinned() {
        val set = receipt()
        val correct = receipt(version = 7, operationId = id(22), prior = set.reference())
        val first = OrdinaryNgQuery(1, 1, 1)
        val older = OrdinaryNgQuery(2, 1, 1, 7)
        val initial = decode(page(first, correct, listOf(correct), 7), first)
        val previous = decode(page(older, correct, listOf(set)), older)
        assertEquals(listOf(attempt), initial.items.map { it.attempt })
        assertEquals(listOf(other), previous.items.map { it.attempt })
        assertEquals(set, previous.history.single().receipt)
        assertNull(previous.historyBeforeDraftVersion)
        malformed(page(first, correct, listOf(correct), 6), first)
        malformed(page(older, correct, listOf(correct)), older)
        malformed(page(first, correct, listOf(correct), 7).replace("totalItems", JsonPrimitive(3)), first)
        for (invalid in listOf<() -> OrdinaryNgQuery>({ OrdinaryNgQuery(0) }, { OrdinaryNgQuery(Int.MAX_VALUE) },
            { OrdinaryNgQuery(pageSize = 21) }, { OrdinaryNgQuery(historyLimit = 101) }, { OrdinaryNgQuery(beforeDraftVersion = 0) })) {
            assertThrows(IllegalArgumentException::class.java) { invalid() }
        }
    }

    @Test fun rejectsCurrentStateAndApplicabilityContradictions() {
        val r = receipt()
        val valid = page(latest = r, history = listOf(r))
        malformed(valid.editItem(0) { replace("currentState", JsonPrimitive("Removed")) })
        malformed(valid.editItem(0) { replace("isRequired", JsonPrimitive(true)) })
        malformed(valid.editItem(0) { replace("blocksSubmit", JsonPrimitive(true)) })
        for (flag in listOf("isCurrentAttempt", "isLatestForAttempt", "hasCurrentOrdinaryNg", "matchesAcceptedNgBasis", "isCurrentlyApplicable")) {
            malformed(valid.editItem(0) { replace("latest", observation(r).replace(flag, JsonPrimitive(false))) })
        }
        val obsolete = receipt(at = WorkAttempt(attempt.taskItemId, id(77)), operationId = id(32))
        val history = observation(obsolete).replace("currentState", JsonPrimitive("AttemptNotCurrent"))
            .replace("isCurrentAttempt", JsonPrimitive(false)).replace("hasCurrentOrdinaryNg", JsonPrimitive(false))
            .replace("matchesAcceptedNgBasis", JsonPrimitive(false)).replace("isCurrentlyApplicable", JsonPrimitive(false))
        val retired = decode(page().replace("history", JsonArray(listOf(history))))
        assertFalse(retired.history.single().currentAttempt)
        assertEquals(OrdinaryNgState.AttemptNotCurrent, retired.history.single().state)
    }

    @Test fun rejectsScopeReceiptHashAndProvenanceDrift() {
        val r = receipt()
        val valid = page(latest = r, history = listOf(r))
        for (key in listOf("taskId", "lineId", "workflowVersion", "draftVersion", "canExecute", "canSubmit", "acceptsFormalInspection")) {
            malformed(valid.replace(key, if (key.startsWith("can") || key == "acceptsFormalInspection") JsonPrimitive(true)
                else JsonPrimitive("different")))
        }
        malformed(valid.editItem(0) { replace("taskItemId", JsonPrimitive(other.taskItemId)) })
        malformed(valid.editItem(0) { replace("inspectionAttemptId", JsonPrimitive(other.inspectionAttemptId)) })
        for ((key, value) in listOf("contentHash" to "C".repeat(64), "contributorUserId" to id(81),
            "sourceWorkContextId" to id(82), "actorUserId" to "bad", "acceptedAtUtc" to "bad",
            "environmentId" to "foreign", "inspectionAttemptId" to other.inspectionAttemptId)) {
            malformed(valid.editItem(0) {
                replace("latest", observation(r).replace("receipt", receiptJson(r).replace(key, JsonPrimitive(value))))
            })
        }
        val badBasis = receiptJson(r).replace("acceptedNgBasis", JsonArray(listOf(ref(sampleRef), ref(sampleRef))))
        malformed(page(latest = r).editItem(0) { replace("latest", observation(r).replace("receipt", badBasis)) })
    }

    @Test fun usesOnlyPinnedGetAndPropagatesDenialAndConflictWithoutFallback() = runBlocking<Unit> {
        TestServer().use { server ->
            val path = "/api/line-patrol/tasks/" + task.taskId + "/shared-draft/ordinary-ng-details"
            val query = OrdinaryNgQuery(2, 1, 20, 7)
            server.enqueue(page(query).toString())
            server.workApi.readOrdinaryNgDetails("Bearer fixture-reader", draft(), query)
            fun verifyRequest() {
                val request = requireNotNull(server.web.takeRequest(4, TimeUnit.SECONDS))
                assertEquals("GET", request.method)
                assertEquals("Bearer fixture-reader", request.getHeader("Authorization"))
                val url = requireNotNull(request.requestUrl)
                assertEquals(path, url.encodedPath)
                assertEquals(mapOf("expectedTaskLineId" to task.lineId, "expectedDraftVersion" to "7",
                    "page" to "2", "pageSize" to "1", "historyLimit" to "20", "beforeDraftVersion" to "7"),
                    url.queryParameterNames.associateWith { url.queryParameter(it) })
            }
            verifyRequest()
            server.enqueue("""{"status":409,"title":"DraftVersionConflict","code":"DraftVersionConflict"}""", 409)
            try {
                server.workApi.readOrdinaryNgDetails("Bearer fixture-reader", draft(), query)
                fail("Expected the exact pinned version conflict")
            } catch (_: OrdinaryNgVersionConflict) {
                verifyRequest()
            }
            for ((status, issue) in listOf(403 to Issue.FORBIDDEN, 401 to Issue.UNAUTHENTICATED, 409 to Issue.CONFLICT)) {
                server.enqueue("""{"code":"OtherProblem"}""", status)
                expectIssue(issue) { server.workApi.readOrdinaryNgDetails("Bearer fixture-reader", draft(), query) }
                verifyRequest()
            }
            server.web.enqueue(MockResponse().setResponseCode(307).setHeader("Location", server.origin.resolve(path).toString()))
            expectIssue(Issue.RESPONSE) { server.workApi.readOrdinaryNgDetails("Bearer fixture-reader", draft(), query) }
            verifyRequest()
            assertEquals(6, server.web.requestCount)
            assertTrue(WorkContextApi::class.java.methods.none { it.getAnnotation(POST::class.java)?.value?.contains("ordinary-ng") == true })
        }
    }

    @Test fun rejectsDuplicateOversizedOrInexactAcceptedSampleEvidence() = runBlocking<Unit> {
        TestServer().use { server ->
            val response = page().toString()
            val sample = draft().sampleBasis.samples.single()
            server.enqueue(response.replaceFirst("{", """{"page":1,"""))
            expectIssue(Issue.RESPONSE) { server.workApi.readOrdinaryNgDetails("Bearer fixture", draft(), OrdinaryNgQuery()) }
            server.enqueue(" ".repeat(MAX_ORDINARY_NG_READ_BYTES.toInt()) + response)
            expectIssue(Issue.RESPONSE) { server.workApi.readOrdinaryNgDetails("Bearer fixture", draft(), OrdinaryNgQuery()) }
            val proof = ServerOperationReceipt(task.taskId, sampleRef.operationId, id(99), 1, sampleRef.contentHash,
                SampleKind.CaptureSample, attempt.taskItemId, attempt.inspectionAttemptId, sample.sampleId,
                sample.key.clientSampleId, source, contributor, id(16), id(17), task.environmentId, task.lineId,
                2, Fixture.NOW, 2, sample.value, false, sampleRef.operationId)
            server.enqueue(Json.encodeToString(proof))
            assertEquals(sampleRef, server.workApi.readOrdinaryNgSampleReference("Bearer fixture", draft(), sample))
            server.enqueue(Json.encodeToString(proof.copy(contributorUserId = id(85))))
            expectIssue(Issue.RESPONSE) { server.workApi.readOrdinaryNgSampleReference("Bearer fixture", draft(), sample) }
            assertEquals(4, server.web.requestCount)
            val requests = List(4) { requireNotNull(server.web.takeRequest(4, TimeUnit.SECONDS)) }
            assertTrue(requests.all { it.method == "GET" })
            assertTrue(requests.takeLast(2).all { it.path?.endsWith("/operations/" + sampleRef.operationId + "/receipt") == true })
        }
    }

    private fun draft(): OnlineDraft {
        val sample = ObservedSample(id(14), LocalSampleKey(attempt, id(15)), ServerSampleValue("Qualitative", null, "NG", null),
            contributor, sampleRef.operationId, sampleRef.operationId, false, true)
        val frozen = listOf(attempt, other).map { FrozenSampleAttempt(it, 1, FrozenSampleType.Qualitative, true, ItemDisposition.Applicable) }
        return OnlineDraft(task, 7, DraftAvailability.SharedDraftInitialized, setOf(attempt, other), setOf(attempt, other),
            false, true, OfflineSampleBasis(task, 7, true, frozen, listOf(sample)), listOf(attempt.taskItemId, other.taskItemId))
    }

    private fun receipt(
        version: Long = 4, operationId: String = id(20), prior: OperationReference? = null,
        value: OrdinaryNgText = OrdinaryNgText(null, null, null), at: WorkAttempt = attempt,
    ): OrdinaryNgReceipt {
        val op = OrdinaryNgOperation(operationId, if (prior == null) OrdinaryNgKind.SetOrdinaryNgDetails
            else OrdinaryNgKind.CorrectOrdinaryNgDetails, at, source, contributor, version - 1, value, prior)
        return OrdinaryNgReceipt(task, op, id((30 + version).toInt()), workHash(OrdinaryNgCanonical.operation(task, op, 1)),
            id(16), id(17), 1, Fixture.NOW, version, listOf(sampleRef))
    }

    private fun page(
        query: OrdinaryNgQuery = OrdinaryNgQuery(), latest: OrdinaryNgReceipt? = null,
        history: List<OrdinaryNgReceipt> = listOfNotNull(latest), cursor: Long? = null,
    ) = buildJsonObject {
        put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", 2); put("draftVersion", 7)
        put("page", query.page); put("pageSize", query.pageSize); put("totalItems", 2)
        val current = listOf(attempt, other).drop((query.page - 1) * query.pageSize).take(query.pageSize)
        put("items", JsonArray(current.map { key ->
            buildJsonObject {
                put("taskItemId", key.taskItemId); put("inspectionAttemptId", key.inspectionAttemptId)
                put("currentState", if (key == attempt) "OrdinaryNG" else "NotCurrentlyNG")
                put("latest", latest?.takeIf { key == attempt }?.let { observation(it) } ?: JsonNull)
                put("isRequired", false); put("blocksSubmit", false)
            }
        }))
        put("history", JsonArray(history.map { observation(it, latest == null || it == latest) }))
        put("historyBeforeDraftVersion", cursor)
        put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
        put("observation", "ReadOnlyPointInTime")
    }

    private fun observation(receipt: OrdinaryNgReceipt, latest: Boolean = true) = buildJsonObject {
        put("receipt", receiptJson(receipt)); put("isCurrentAttempt", true); put("isLatestForAttempt", latest)
        put("currentState", "OrdinaryNG"); put("hasCurrentOrdinaryNg", true); put("matchesAcceptedNgBasis", true)
        put("isCurrentlyApplicable", latest)
    }

    private fun receiptJson(r: OrdinaryNgReceipt) = buildJsonObject {
        val op = r.operation
        put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", 2); put("environmentId", task.environmentId)
        put("operationId", op.operationId); put("batchId", r.batchId); put("canonicalizationVersion", 1)
        put("contentHash", r.contentHash); put("kind", op.kind.name); put("taskItemId", op.attempt.taskItemId)
        put("inspectionAttemptId", op.attempt.inspectionAttemptId); put("sourceWorkContextId", op.sourceWorkContextId)
        put("contributorUserId", op.contributorUserId); put("actorUserId", r.actorUserId); put("actorPersonalSessionId", r.actorPersonalSessionId)
        put("baseDraftVersion", op.baseDraftVersion); put("originLocalEditVersion", r.originLocalEditVersion)
        put("acceptedAtUtc", r.acceptedAtUtc); put("draftVersion", r.draftVersion); put("predecessor", op.predecessor?.let(::ref) ?: JsonNull)
        put("ngType", op.text.ngType); put("ngReason", op.text.ngReason); put("remarks", op.text.remarks)
        put("acceptedNgBasis", JsonArray(r.acceptedNgBasis.map(::ref)))
    }

    private fun ref(reference: OperationReference) = buildJsonObject {
        put("operationId", reference.operationId); put("contentHash", reference.contentHash)
    }
    private fun JsonObject.replace(name: String, value: JsonElement) = JsonObject(this + (name to value))
    private fun JsonObject.editItem(index: Int, edit: JsonObject.() -> JsonObject) =
        replace("items", JsonArray(getValue("items").jsonArray.mapIndexed { i, e -> if (i == index) e.jsonObject.edit() else e }))
    private fun decode(body: JsonObject, query: OrdinaryNgQuery = OrdinaryNgQuery()) = body.decodeOrdinaryNgDetails(draft(), query)
    private fun malformed(body: JsonObject, query: OrdinaryNgQuery = OrdinaryNgQuery()) {
        assertEquals(Issue.RESPONSE, assertThrows(PatrolFailure::class.java) { decode(body, query) }.issue)
    }
    private fun id(n: Int) = "00000000-0000-0000-0000-" + n.toString().padStart(12, '0')
}
