using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205OrdinaryNgDetailsDraft
{
    private void Protect(MigrationBuilder migration)
    {
        const string table = "OrdinaryNgDetailsOperations";
        const string receipt = "NEW.\"ReceiptJson\"";
        const string canonical = "NEW.\"CanonicalPayload\"";
        const string basis = "NEW.\"NgBasisJson\"";
        const string batch = "b.\"ReceiptJson\"";
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        string Value(string json, string path) => pg
            ? $"({json}::jsonb #>> '{{{path.Replace('.', ',')}}}')"
            : $"json_extract({json}, '$.{path}')";
        string Element(string json, string path) => pg
            ? $"({json}::jsonb #> '{{{path.Replace('.', ',')}}}')"
            : $"json_extract({json}, '$.{path.Replace(".0", "[0]")}')";
        string Same(string a, string b) => pg ? $"({a})::jsonb=({b})::jsonb" : $"json({a})=json({b})";
        string Size(string json) => pg ? $"jsonb_array_length(({json})::jsonb)" : $"json_array_length({json})";
        string GuidEquals(string json, string column) => $"lower({json})=lower(CAST({column} AS TEXT))";
        string NullableEquals(string value, string column) => pg ? $"{value} IS NOT DISTINCT FROM {column}" : $"{value} IS {column}";

        var scope = """
            SELECT 1 FROM "WorkContexts" c
            JOIN "PatrolTasks" t ON t."Id"=c."TaskId"
            JOIN "PatrolTaskItems" i ON i."PatrolTaskId"=t."Id"
            JOIN "InspectionAttemptApplicabilities" a ON a."PatrolTaskItemId"=i."Id"
            JOIN "ApplicationUsers" u ON u."Id"=c."UserId"
            JOIN "ApplicationUsers" v ON v."Id"=NEW."ActorUserId"
            JOIN "SharedDrafts" d ON d."TaskId"=t."Id"
            JOIN "AcceptedBatchReceipts" b ON b."TaskId"=t."Id" AND b."BatchId"=NEW."BatchId"
            WHERE c."Id"=NEW."SourceWorkContextId" AND t."Id"=NEW."TaskId" AND c."UserId"=NEW."ContributorUserId"
              AND c."LineId"=NEW."LineId" AND c."EnvironmentId"=NEW."EnvironmentId" AND c."WorkflowVersion"=2
              AND c."SecurityRevokedAtUtc" IS NULL AND u."IsActive" AND u."Role"='IPQA'
              AND v."IsActive" AND v."Role"='IPQA' AND NOT v."MustChangePassword"
              AND t."LineId"=NEW."LineId" AND t."WorkflowVersion"=2 AND t."CurrentRevisionNo"=0
              AND t."Status" IN ('PendingInspection','InProgress')
              AND i."Id"=NEW."TaskItemId" AND a."InspectionAttemptId"=NEW."InspectionAttemptId"
              AND d."DraftVersion"=NEW."DraftVersion" AND b."DraftVersion"=NEW."DraftVersion"
            """;
        scope += $" AND CAST({Value(batch, "receiptVersion")} AS BIGINT)=8" +
                 $" AND {Size(Element(batch, "ordinaryNgDetailsOperations"))}=1" +
                 $" AND {Same(Element(batch, "ordinaryNgDetailsOperations.0"), receipt)}" +
                 $" AND {GuidEquals(Value(batch, "uploadedByUserId"), "NEW.\"ActorUserId\"")}" +
                 $" AND {GuidEquals(Value(batch, "uploadedPersonalSessionId"), "NEW.\"ActorPersonalSessionId\"")}" +
                 $" AND {Same(Element(receipt, "acceptedNgBasis"), basis)}";
        foreach (var (field, column) in new[] {
            ("taskId", "TaskId"), ("operationId", "OperationId"), ("batchId", "BatchId"), ("taskItemId", "TaskItemId"),
            ("inspectionAttemptId", "InspectionAttemptId"), ("sourceWorkContextId", "SourceWorkContextId"),
            ("contributorUserId", "ContributorUserId"), ("actorUserId", "ActorUserId"), ("actorPersonalSessionId", "ActorPersonalSessionId"), ("lineId", "LineId")
        })
            scope += $" AND {GuidEquals(Value(receipt, field), $"NEW.\"{column}\"")}";
        foreach (var (field, column) in new[] {
            ("draftVersion", "DraftVersion"), ("baseDraftVersion", "BaseDraftVersion"), ("originLocalEditVersion", "OriginLocalEditVersion"),
            ("workflowVersion", "WorkflowVersion"), ("canonicalizationVersion", "CanonicalizationVersion")
        })
            scope += $" AND CAST({Value(receipt, field)} AS BIGINT)=NEW.\"{column}\"";
        foreach (var (field, column) in new[] { ("contentHash", "ContentHash"), ("kind", "Kind"), ("environmentId", "EnvironmentId") })
            scope += $" AND {Value(receipt, field)}=NEW.\"{column}\"";
        foreach (var (field, column) in new[] { ("ngType", "NgType"), ("ngReason", "NgReason"), ("remarks", "Remarks") })
        {
            var target = $"NEW.\"{column}\"";
            scope += $" AND ({NullableEquals(Value(receipt, field), target)}) AND ({NullableEquals(Value(canonical, field), target)})";
        }
        var priorId = $"lower({Value(receipt, "predecessor.operationId")})";
        var priorColumn = """lower(CAST(NEW."PredecessorOperationId" AS TEXT))""";
        scope += $" AND ({NullableEquals(priorId, priorColumn)})" +
                 $" AND ({NullableEquals(Value(receipt, "predecessor.contentHash"), "NEW.\"PredecessorHash\"")})";

        const string scopeMatch = """
            "TaskId"=NEW."TaskId" AND "TaskItemId"=NEW."TaskItemId" AND "InspectionAttemptId"=NEW."InspectionAttemptId"
            """;
        var latest = $"SELECT \"OperationId\" FROM \"{table}\" WHERE {scopeMatch} ORDER BY \"DraftVersion\" DESC LIMIT 1";
        var projection = $"""
            (NEW."Kind"='SetOrdinaryNgDetails' AND NOT EXISTS ({latest}))
            OR (NEW."Kind"='CorrectOrdinaryNgDetails' AND NEW."PredecessorOperationId"=({latest})
                AND NEW."DraftVersion">(SELECT "DraftVersion" FROM "{table}"
                    WHERE "TaskId"=NEW."TaskId" AND "OperationId"=NEW."PredecessorOperationId"))
            """;
        var values = """
            FROM "SharedSamples" s
            JOIN "SharedSampleProjections" p ON p."SampleId"=s."Id"
            JOIN "ContributionOperations" v ON v."TaskId"=p."TaskId" AND v."OperationId"=p."ValueOperationId"
            WHERE s."TaskId"=NEW."TaskId" AND s."TaskItemId"=NEW."TaskItemId" AND s."InspectionAttemptId"=NEW."InspectionAttemptId"
              AND v."TaskItemId"=s."TaskItemId" AND v."InspectionAttemptId"=s."InspectionAttemptId" AND v."SampleId"=s."Id"
              AND NOT p."IsRemoved" AND p."Judgment"='NG'
            """;
        values += $" AND {Value("v.\"ReceiptJson\"", "value.judgment")}='NG'";
        var refs = pg ? $"jsonb_array_elements({basis}::jsonb) j(value)" : $"json_each({basis}) j";
        var refId = Value("j.value", "operationId");
        var refHash = Value("j.value", "contentHash");
        var length = Size(basis);
        var validBasis = $"""
            NOT EXISTS (SELECT 1 FROM "ItemRemovalOperations" WHERE {scopeMatch})
            AND NOT EXISTS (SELECT 1 FROM "UnableToMeasureProjections" WHERE {scopeMatch} AND "IsActive")
            AND {length} BETWEEN 1 AND 50 AND {length}=(SELECT COUNT(*) {values})
            AND {length}=(SELECT COUNT(DISTINCT lower({refId})) FROM {refs})
            AND NOT EXISTS (SELECT 1 FROM {refs} WHERE NOT EXISTS (SELECT 1 {values}
                AND {GuidEquals(refId, "v.\"OperationId\"")} AND {refHash}=v."ContentHash"))
            """;

        var invalid = $"NOT EXISTS ({scope}) OR NOT COALESCE(({projection}),false) OR NOT COALESCE(({validBasis}),false)";
        const string registered = """
            EXISTS (SELECT 1 FROM "AcceptedOperationIdentities" WHERE "TaskId"=NEW."TaskId" AND "OperationId"=NEW."OperationId")
            """;
        const string register = """
            INSERT INTO "AcceptedOperationIdentities" ("TaskId","OperationId","Domain")
            VALUES (NEW."TaskId",NEW."OperationId",'OrdinaryNgDetails');
            """;
        if (pg)
        {
            migration.Sql($"""
                CREATE FUNCTION ipqc_ordinary_ng_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF {registered} THEN RAISE EXCEPTION 'OperationIdentityConflict' USING ERRCODE='23514'; END IF;
                    IF {invalid} THEN RAISE EXCEPTION 'OrdinaryNgDetailsScopeConflict' USING ERRCODE='23514'; END IF;
                    {register}
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER "TR_OrdNg_Insert" BEFORE INSERT ON "{table}" FOR EACH ROW EXECUTE FUNCTION ipqc_ordinary_ng_insert();
                CREATE TRIGGER "TR_OrdNg_Immutable" BEFORE UPDATE OR DELETE ON "{table}" FOR EACH ROW
                    EXECUTE FUNCTION ipqc_reject_accepted_history_change();
                CREATE TRIGGER "TR_OrdNg_Truncate" BEFORE TRUNCATE ON "{table}" FOR EACH STATEMENT
                    EXECUTE FUNCTION ipqc_reject_accepted_history_change();
                """);
        }
        else
        {
            migration.Sql($"""
                CREATE TRIGGER "TR_OrdNg_Insert" BEFORE INSERT ON "{table}" BEGIN
                    SELECT RAISE(ABORT,'OperationIdentityConflict') WHERE {registered};
                    SELECT RAISE(ABORT,'OrdinaryNgDetailsScopeConflict') WHERE {invalid};
                    {register}
                END;
                CREATE TRIGGER "TR_OrdNg_Update" BEFORE UPDATE ON "{table}" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;
                CREATE TRIGGER "TR_OrdNg_Delete" BEFORE DELETE ON "{table}" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;
                """);
        }
    }
}
