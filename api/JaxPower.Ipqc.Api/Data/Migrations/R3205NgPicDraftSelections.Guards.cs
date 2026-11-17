using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205NgPicDraftSelections
{
    private void Protect(MigrationBuilder m) {
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        string J(string json, string path) => pg ? $"({json}::jsonb #>> '{{{path.Replace('.', ',')}}}')" : $"json_extract({json},'$.{path}')";
        string Count(string json, string path) => pg ? $"jsonb_array_length({json}::jsonb #> '{{{path.Replace('.', ',')}}}')" : $"json_array_length({json},'$.{path}')";
        string Array(string json, string path, string alias) => pg ? $"jsonb_array_elements({json}::jsonb #> '{{{path.Replace('.', ',')}}}') {alias}(value)" : $"json_each({json},'$.{path}') {alias}";
        string Same(string a, string b) => pg ? $"{a}::jsonb={b}::jsonb" : $"json({a})=json({b})";
        string GuidEq(string value, string column) => $"lower({value})=lower(CAST({column} AS TEXT))";
        void Guard(string name, string table, string action, string invalid, string error, string extra = "") {
            if (pg) {
                m.Sql($"CREATE FUNCTION ipqc_{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {invalid} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; {extra} RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_{name}();");
            } else m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {invalid}; {extra} END;");
        }
        Guard("ng_pic_identity", "NgPicSelectionOperations", "INSERT", "EXISTS (SELECT 1 FROM \"AcceptedOperationIdentities\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\")", "OperationIdentityConflict",
            "INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'NgPicSelection');");
        const string receipt = "NEW.\"ReceiptJson\"";
        const string canonical = "NEW.\"CanonicalPayload\"";
        var scope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"PatrolTaskItems\" i ON i.\"PatrolTaskId\"=t.\"Id\" JOIN \"InspectionAttemptApplicabilities\" a ON a.\"PatrolTaskItemId\"=i.\"Id\" JOIN \"ApplicationUsers\" u ON u.\"Id\"=c.\"UserId\" JOIN \"ApplicationUsers\" v ON v.\"Id\"=NEW.\"ActorUserId\" JOIN \"SharedDrafts\" d ON d.\"TaskId\"=t.\"Id\" JOIN \"AcceptedBatchReceipts\" b ON b.\"TaskId\"=t.\"Id\" AND b.\"BatchId\"=NEW.\"BatchId\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"WorkflowVersion\"=2 AND c.\"SecurityRevokedAtUtc\" IS NULL AND u.\"IsActive\" AND u.\"Role\"='IPQA' AND v.\"IsActive\" AND v.\"Role\"='IPQA' AND NOT v.\"MustChangePassword\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND t.\"CurrentRevisionNo\"=0 AND t.\"Status\" IN ('PendingInspection','InProgress') AND i.\"Id\"=NEW.\"TaskItemId\" AND a.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND d.\"DraftVersion\"=NEW.\"DraftVersion\" AND b.\"DraftVersion\"=NEW.\"DraftVersion\"";
        var batchReceipt = pg ? "(b.\"ReceiptJson\"::jsonb #> '{picSelectionOperations,0}')" : "json_extract(b.\"ReceiptJson\",'$.picSelectionOperations[0]')";
        scope += $" AND CAST({J("b.\"ReceiptJson\"", "receiptVersion")} AS BIGINT)=7 AND {Count("b.\"ReceiptJson\"", "picSelectionOperations")}=1 AND {Same(batchReceipt, receipt)}" +
            $" AND {GuidEq(J("b.\"ReceiptJson\"", "uploadedByUserId"), "NEW.\"ActorUserId\"")} AND {GuidEq(J("b.\"ReceiptJson\"", "uploadedPersonalSessionId"), "NEW.\"ActorPersonalSessionId\"")}";
        foreach (var (jsonField, column) in new[] { ("taskId", "TaskId"), ("operationId", "OperationId"), ("batchId", "BatchId"), ("taskItemId", "TaskItemId"), ("inspectionAttemptId", "InspectionAttemptId"), ("sourceWorkContextId", "SourceWorkContextId"), ("contributorUserId", "ContributorUserId"), ("actorUserId", "ActorUserId"), ("actorPersonalSessionId", "ActorPersonalSessionId"), ("lineId", "LineId") })
            scope += $" AND {GuidEq(J(receipt, jsonField), "NEW.\"" + column + "\"")}";
        foreach (var (jsonField, column) in new[] { ("contentHash", "ContentHash"), ("kind", "Kind"), ("environmentId", "EnvironmentId") }) scope += $" AND {J(receipt, jsonField)}=NEW.\"{column}\"";
        foreach (var (jsonField, column) in new[] { ("baseDraftVersion", "BaseDraftVersion"), ("originLocalEditVersion", "OriginLocalEditVersion"), ("draftVersion", "DraftVersion"), ("workflowVersion", "WorkflowVersion"), ("canonicalizationVersion", "CanonicalizationVersion") })
            scope += $" AND CAST({J(receipt, jsonField)} AS BIGINT)=NEW.\"{column}\"";
        var ngObject = pg ? "(NEW.\"ReceiptJson\"::jsonb -> 'ngObservation')" : "json_extract(NEW.\"ReceiptJson\",'$.ngObservation')";
        scope += $" AND {Count(receipt, "selectedPics")}=NEW.\"PicCount\" AND {Count(canonical, "picIds")}=NEW.\"PicCount\" AND {Same(ngObject, "NEW.\"NgBasisJson\"")}";
        const string removed = "EXISTS (SELECT 1 FROM \"ItemRemovalOperations\" r WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"TaskItemId\"=NEW.\"TaskItemId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\")";
        const string activeUnable = "SELECT 1 FROM \"UnableToMeasureProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"TaskItemId\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"IsActive\"";
        var ngRows = "FROM \"SharedSamples\" s JOIN \"SharedSampleProjections\" p ON p.\"SampleId\"=s.\"Id\" JOIN \"ContributionOperations\" v ON v.\"TaskId\"=p.\"TaskId\" AND v.\"OperationId\"=p.\"ValueOperationId\" WHERE s.\"TaskId\"=NEW.\"TaskId\" AND s.\"TaskItemId\"=NEW.\"TaskItemId\" AND s.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND v.\"TaskItemId\"=s.\"TaskItemId\" AND v.\"InspectionAttemptId\"=s.\"InspectionAttemptId\" AND v.\"SampleId\"=s.\"Id\" AND NOT p.\"IsRemoved\" AND p.\"Judgment\"='NG' AND " + J("v.\"ReceiptJson\"", "value.judgment") + "='NG'";
        var refs = Array("NEW.\"NgBasisJson\"", "references", "j"); var refId = J("j.value", "operationId"); var refHash = J("j.value", "contentHash");
        var refCount = Count("NEW.\"NgBasisJson\"", "references");
        var ordinary = $"NEW.\"NgKind\"='OrdinaryNG' AND NOT EXISTS ({activeUnable}) AND {refCount} BETWEEN 1 AND 50 AND {refCount}=(SELECT COUNT(*) {ngRows}) AND NOT EXISTS (SELECT 1 FROM {refs} WHERE NOT EXISTS (SELECT 1 {ngRows} AND {GuidEq(refId, "v.\"OperationId\"")} AND {refHash}=v.\"ContentHash\"))";
        var unable = $"NEW.\"NgKind\"='UnableToMeasureNG' AND {refCount}=1 AND EXISTS (SELECT 1 FROM {refs} JOIN \"UnableToMeasureProjections\" p ON p.\"TaskId\"=NEW.\"TaskId\" AND p.\"TaskItemId\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"IsActive\" JOIN \"UnableToMeasureOperations\" v ON v.\"TaskId\"=p.\"TaskId\" AND v.\"OperationId\"=p.\"LastOperationId\" WHERE {GuidEq(refId, "v.\"OperationId\"")} AND {refHash}=v.\"ContentHash\")";
        var ngValid = $"NOT ({removed}) AND {J("NEW.\"NgBasisJson\"", "kind")}=NEW.\"NgKind\" AND {refCount}=(SELECT COUNT(DISTINCT lower({refId})) FROM {refs}) AND (({ordinary}) OR ({unable}))";
        const string projection = "SELECT 1 FROM \"NgPicSelectionProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"TaskItemId\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        const string predecessor = "SELECT 1 FROM \"NgPicSelectionProjections\" p JOIN \"NgPicSelectionOperations\" o ON o.\"TaskId\"=p.\"TaskId\" AND o.\"OperationId\"=p.\"LastOperationId\" WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"TaskItemId\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND o.\"OperationId\"=NEW.\"PredecessorOperationId\" AND o.\"ContentHash\"=NEW.\"PredecessorHash\" AND o.\"DraftVersion\"<NEW.\"DraftVersion\"";
        Guard("ng_pic_scope", "NgPicSelectionOperations", "INSERT", $"NOT EXISTS ({scope}) OR NOT COALESCE(({ngValid}),false) OR (NEW.\"Kind\"='SelectNgPics' AND EXISTS ({projection})) OR (NEW.\"Kind\"='CorrectNgPics' AND NOT EXISTS ({predecessor}))", "NgPicScopeConflict");
        var snapshots = Array("o.\"ReceiptJson\"", "selectedPics", "j");
        var requested = pg ? "jsonb_array_elements_text(o.\"CanonicalPayload\"::jsonb -> 'picIds') k(id)" : "json_each(o.\"CanonicalPayload\",'$.picIds') k";
        var requestedId = pg ? "k.id" : "k.value";
        var member = "SELECT 1 FROM \"NgPicSelectionOperations\" o JOIN \"SharedDrafts\" d ON d.\"TaskId\"=o.\"TaskId\" JOIN \"PicRosterRevisions\" r ON r.\"PicId\"=NEW.\"PicId\" AND r.\"Revision\"=NEW.\"RosterRevision\" WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"OperationId\" AND d.\"DraftVersion\"=o.\"DraftVersion\" AND r.\"IsActive\" AND r.\"Revision\"=(SELECT MAX(v.\"Revision\") FROM \"PicRosterRevisions\" v WHERE v.\"PicId\"=r.\"PicId\") AND r.\"Name\"=NEW.\"Name\" AND r.\"Department\"=NEW.\"Department\"" +
            $" AND EXISTS (SELECT 1 FROM {requested} WHERE {GuidEq(requestedId, "NEW.\"PicId\"")}) AND EXISTS (SELECT 1 FROM {snapshots} WHERE {GuidEq(J("j.value", "picId"), "NEW.\"PicId\"")} AND CAST({J("j.value", "rosterRevision")} AS BIGINT)=NEW.\"RosterRevision\" AND {J("j.value", "name")}=NEW.\"Name\" AND {J("j.value", "department")}=NEW.\"Department\")";
        Guard("ng_pic_member", "NgPicSelectionMembers", "INSERT", $"NOT EXISTS ({member})", "NgPicSnapshotConflict");
        const string pointer = "SELECT 1 FROM \"NgPicSelectionOperations\" o JOIN \"SharedDrafts\" d ON d.\"TaskId\"=o.\"TaskId\" WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"TaskItemId\"=NEW.\"TaskItemId\" AND o.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND o.\"DraftVersion\"=d.\"DraftVersion\" AND o.\"PicCount\"=(SELECT COUNT(*) FROM \"NgPicSelectionMembers\" m WHERE m.\"TaskId\"=o.\"TaskId\" AND m.\"OperationId\"=o.\"OperationId\")";
        Guard("ng_pic_projection_insert", "NgPicSelectionProjections", "INSERT", $"NOT EXISTS ({pointer} AND o.\"Kind\"='SelectNgPics')", "NgPicProjectionConflict");
        Guard("ng_pic_projection_update", "NgPicSelectionProjections", "UPDATE", $"OLD.\"TaskId\"<>NEW.\"TaskId\" OR OLD.\"TaskItemId\"<>NEW.\"TaskItemId\" OR OLD.\"InspectionAttemptId\"<>NEW.\"InspectionAttemptId\" OR NOT EXISTS ({pointer} AND o.\"Kind\"='CorrectNgPics' AND o.\"PredecessorOperationId\"=OLD.\"LastOperationId\")", "NgPicProjectionConflict");
        foreach (var table in new[] { "NgPicSelectionOperations", "NgPicSelectionMembers", "NgPicSelectionProjections" })
            foreach (var action in table == "NgPicSelectionProjections" ? new[] { "DELETE" } : new[] { "UPDATE", "DELETE" })
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'NG PIC history is immutable'); END;");
    }
}
