using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205UnableToMeasureNgDraft
{
    private void Protect(MigrationBuilder m)
    {
        // Derived exclusion keys only; historical operations/receipts are never rewritten.
        m.Sql("INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") SELECT \"TaskId\",\"OperationId\",\"Domain\" FROM \"DraftOperationKeys\";");
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        void Guard(string function, string trigger, string table, string action, string condition, string error, string extra = "") {
            if (pg) {
                m.Sql($"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; {extra} RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"{trigger}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION {function}();");
            } else m.Sql($"CREATE TRIGGER \"{trigger}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {condition}; {extra} END;");
        }
        const string key = "SELECT 1 FROM \"AcceptedOperationIdentities\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\"";
        const string priorKey = "SELECT 1 FROM \"DraftOperationKeys\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\"";
        Guard("ipqc_index_existing_accepted_key", "TR_DraftOperationKeys_AllDomains", "DraftOperationKeys", "INSERT", $"EXISTS ({key}) AND NOT EXISTS ({priorKey})", "OperationIdentityConflict",
            $"INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") SELECT NEW.\"TaskId\",NEW.\"OperationId\",NEW.\"Domain\" WHERE NOT EXISTS ({priorKey});");
        Guard("ipqc_index_unable_key", "TR_Unable_Identity", "UnableToMeasureOperations", "INSERT", $"EXISTS ({key})", "OperationIdentityConflict",
            "INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'UnableToMeasureNG');");
        const string active = "SELECT 1 FROM \"UnableToMeasureProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"IsActive\"";
        foreach (var table in new[] { "ContributionOperations", "ItemRemovalOperations" })
            Guard("ipqc_" + (table == "ContributionOperations" ? "sample" : "removal") + "_unable_guard", "TR_" + table + "_UnableGuard", table, "INSERT", $"EXISTS ({active})", "DraftItemUnableToMeasureNG");
        const string scope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"PatrolTaskItems\" i ON i.\"PatrolTaskId\"=t.\"Id\" JOIN \"InspectionAttemptApplicabilities\" p ON p.\"PatrolTaskItemId\"=i.\"Id\" JOIN \"AcceptedBatchReceipts\" b ON b.\"TaskId\"=t.\"Id\" AND b.\"BatchId\"=NEW.\"BatchId\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND i.\"Id\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND b.\"DraftVersion\"=NEW.\"DraftVersion\"";
        const string removed = "SELECT 1 FROM \"ItemRemovalOperations\" r WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        const string anyProjection = "SELECT 1 FROM \"UnableToMeasureProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        const string predecessor = "SELECT 1 FROM \"UnableToMeasureProjections\" p JOIN \"UnableToMeasureOperations\" o ON o.\"TaskId\"=p.\"TaskId\" AND o.\"OperationId\"=p.\"LastOperationId\" WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"TaskItemId\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"LastOperationId\"=NEW.\"PredecessorOperationId\" AND o.\"ContentHash\"=NEW.\"PredecessorHash\" AND o.\"DraftVersion\"<NEW.\"DraftVersion\" AND ((NEW.\"Kind\"='SetUnableToMeasureNG' AND NOT p.\"IsActive\") OR (NEW.\"Kind\"<>'SetUnableToMeasureNG' AND p.\"IsActive\"))";
        const string evidence = "SELECT 1 FROM \"DraftAttachmentOperations\" r JOIN \"DraftAttachmentProjections\" p ON p.\"TaskId\"=r.\"TaskId\" AND p.\"AttachmentId\"=r.\"AttachmentId\" JOIN \"AttachmentAvailabilities\" v ON v.\"AttachmentId\"=r.\"AttachmentId\" JOIN \"InspectionAttachments\" a ON a.\"Id\"=r.\"AttachmentId\" WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"OperationId\"=NEW.\"EvidenceOperationId\" AND r.\"AttachmentId\"=NEW.\"AttachmentId\" AND r.\"ContentHash\"=NEW.\"EvidenceHash\" AND r.\"Kind\"='AssociateAttachment' AND r.\"TaskItemId\"=NEW.\"TaskItemId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND r.\"LineId\"=NEW.\"LineId\" AND r.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND r.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND p.\"LastOperationId\"=r.\"OperationId\" AND p.\"IsAttached\" AND v.\"State\"='Ready' AND v.\"AcceptedAttachmentId\"=r.\"AttachmentId\" AND a.\"TaskId\"=NEW.\"TaskId\" AND a.\"TaskItemId\"=NEW.\"TaskItemId\" AND a.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        Guard("ipqc_unable_scope", "TR_Unable_Scope", "UnableToMeasureOperations", "INSERT",
            $"NOT EXISTS ({scope}) OR EXISTS ({removed}) OR (NEW.\"PredecessorOperationId\" IS NULL AND EXISTS ({anyProjection})) OR (NEW.\"PredecessorOperationId\" IS NOT NULL AND NOT EXISTS ({predecessor})) OR (NEW.\"Kind\"<>'ClearUnableToMeasureNG' AND NOT EXISTS ({evidence}))", "UnableScopeConflict");
        const string projection = "SELECT 1 FROM \"UnableToMeasureOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"TaskItemId\"=NEW.\"TaskItemId\" AND o.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND NEW.\"IsActive\"=(o.\"Kind\"<>'ClearUnableToMeasureNG')";
        foreach (var action in new[] { "INSERT", "UPDATE" }) {
            var change = action == "INSERT" ? "" : " OR OLD.\"TaskId\"<>NEW.\"TaskId\" OR OLD.\"TaskItemId\"<>NEW.\"TaskItemId\" OR OLD.\"InspectionAttemptId\"<>NEW.\"InspectionAttemptId\" OR NOT EXISTS (SELECT 1 FROM \"UnableToMeasureOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"PredecessorOperationId\"=OLD.\"LastOperationId\")";
            Guard("ipqc_unable_projection_" + action.ToLowerInvariant(), "TR_UnableProjection_" + action, "UnableToMeasureProjections", action, $"NOT EXISTS ({projection}){change}", "UnableProjectionConflict");
        }
        foreach (var table in new[] { "AcceptedOperationIdentities", "UnableToMeasureOperations", "UnableToMeasureProjections" })
            foreach (var action in table == "UnableToMeasureProjections" ? new[] { "DELETE" } : new[] { "UPDATE", "DELETE" }) {
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
            }
    }
}
