using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205SharedTaskBasicInformationDraft
{
    private void Protect(MigrationBuilder m) {
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        void Guard(string name, string table, string action, string condition, string error, string extra = "") {
            if (pg) {
                m.Sql($"CREATE FUNCTION ipqc_{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; {extra} RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_{name}();");
            } else m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {condition}; {extra} END;");
        }
        Guard("header_identity", "TaskHeaderOperations", "INSERT",
            "EXISTS (SELECT 1 FROM \"AcceptedOperationIdentities\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\")", "OperationIdentityConflict",
            "INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'BasicInformationField');");
        var batchVersion = pg ? "(b.\"ReceiptJson\"::jsonb ->> 'receiptVersion')='6'" : "json_extract(b.\"ReceiptJson\",'$.receiptVersion')=6";
        var batchCount = pg ? "jsonb_array_length(b.\"ReceiptJson\"::jsonb -> 'headerOperations')=1" : "json_array_length(b.\"ReceiptJson\",'$.headerOperations')=1";
        var batchOperation = pg ? "(b.\"ReceiptJson\"::jsonb #>> '{headerOperations,0,operationId}')" : "json_extract(b.\"ReceiptJson\",'$.headerOperations[0].operationId')";
        var sameReceipt = pg ? "(b.\"ReceiptJson\"::jsonb #> '{headerOperations,0}')=NEW.\"ReceiptJson\"::jsonb" : "json_extract(b.\"ReceiptJson\",'$.headerOperations[0]')=json(NEW.\"ReceiptJson\")";
        string Text(string property) => pg ? $"(NEW.\"ReceiptJson\"::jsonb ->> '{property}')" : $"json_extract(NEW.\"ReceiptJson\",'$.{property}')";
        var valueMatch = pg ? $"{Text("value")} IS NOT DISTINCT FROM NEW.\"Value\"" : $"{Text("value")} IS NEW.\"Value\"";
        var scope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"ApplicationUsers\" u ON u.\"Id\"=c.\"UserId\" JOIN \"ApplicationUsers\" a ON a.\"Id\"=NEW.\"ActorUserId\" JOIN \"AcceptedBatchReceipts\" b ON b.\"TaskId\"=t.\"Id\" AND b.\"BatchId\"=NEW.\"BatchId\" JOIN \"SharedDrafts\" d ON d.\"TaskId\"=t.\"Id\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND c.\"SecurityRevokedAtUtc\" IS NULL AND u.\"IsActive\" AND u.\"Role\"='IPQA' AND a.\"IsActive\" AND a.\"Role\"='IPQA' AND NOT a.\"MustChangePassword\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND t.\"CurrentRevisionNo\"=0 AND t.\"Status\" IN ('PendingInspection','InProgress') AND b.\"DraftVersion\"=NEW.\"DraftVersion\" AND d.\"DraftVersion\"=NEW.\"DraftVersion\"" +
            $" AND {batchVersion} AND {batchCount} AND lower({batchOperation})=lower(CAST(NEW.\"OperationId\" AS TEXT)) AND {sameReceipt}" +
            $" AND {Text("contentHash")}=NEW.\"ContentHash\" AND {Text("field")}=NEW.\"Field\" AND {Text("kind")}=NEW.\"Kind\" AND ({valueMatch})" +
            " AND b.\"ReceiptJson\" IS NOT NULL";
        // Actual first uploader/family in the immutable common receipt must match the field fact.
        var actor = pg ? "(b.\"ReceiptJson\"::jsonb ->> 'uploadedByUserId')" : "json_extract(b.\"ReceiptJson\",'$.uploadedByUserId')";
        var family = pg ? "(b.\"ReceiptJson\"::jsonb ->> 'uploadedPersonalSessionId')" : "json_extract(b.\"ReceiptJson\",'$.uploadedPersonalSessionId')";
        scope += $" AND lower({actor})=lower(CAST(NEW.\"ActorUserId\" AS TEXT)) AND lower({family})=lower(CAST(NEW.\"ActorPersonalSessionId\" AS TEXT))";
        const string projection = "SELECT 1 FROM \"TaskHeaderProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"Field\"=NEW.\"Field\"";
        const string predecessor = "SELECT 1 FROM \"TaskHeaderProjections\" p JOIN \"TaskHeaderOperations\" o ON o.\"TaskId\"=p.\"TaskId\" AND o.\"OperationId\"=p.\"LastOperationId\" WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"Field\"=NEW.\"Field\" AND o.\"OperationId\"=NEW.\"PredecessorOperationId\" AND o.\"Field\"=NEW.\"Field\" AND o.\"ContentHash\"=NEW.\"PredecessorHash\" AND o.\"DraftVersion\"<NEW.\"DraftVersion\"";
        Guard("header_scope", "TaskHeaderOperations", "INSERT",
            $"NOT EXISTS ({scope}) OR (NEW.\"Kind\"='CaptureBasicInformationField' AND EXISTS ({projection})) OR (NEW.\"Kind\"='CorrectBasicInformationField' AND NOT EXISTS ({predecessor}))", "BasicInformationScopeConflict");
        const string pointer = "SELECT 1 FROM \"TaskHeaderOperations\" o JOIN \"SharedDrafts\" d ON d.\"TaskId\"=o.\"TaskId\" WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"Field\"=NEW.\"Field\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND d.\"DraftVersion\"=o.\"DraftVersion\"";
        Guard("header_projection_insert", "TaskHeaderProjections", "INSERT", $"NOT EXISTS ({pointer} AND o.\"Kind\"='CaptureBasicInformationField')", "BasicInformationProjectionConflict");
        Guard("header_projection_update", "TaskHeaderProjections", "UPDATE",
            $"OLD.\"TaskId\"<>NEW.\"TaskId\" OR OLD.\"Field\"<>NEW.\"Field\" OR NOT EXISTS ({pointer} AND o.\"Kind\"='CorrectBasicInformationField' AND o.\"PredecessorOperationId\"=OLD.\"LastOperationId\")", "BasicInformationProjectionConflict");
        foreach (var table in new[] { "TaskHeaderOperations", "TaskHeaderProjections" })
            foreach (var action in table == "TaskHeaderOperations" ? new[] { "UPDATE", "DELETE" } : new[] { "DELETE" })
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
    }
}
