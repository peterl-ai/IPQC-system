using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3206AtomicDraftAttachmentReferences
{
    private void Protect(MigrationBuilder m)
    {
        m.Sql("INSERT INTO \"DraftOperationKeys\" (\"TaskId\",\"OperationId\",\"Domain\") SELECT \"TaskId\",\"OperationId\",\"Domain\" FROM \"SharedOperationIdentities\";");
        const string exists = "SELECT 1 FROM \"DraftOperationKeys\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\"";
        const string scope = "SELECT 1 FROM \"InspectionAttachments\" a JOIN \"AttachmentAcceptances\" s ON s.\"AttachmentId\"=a.\"Id\" JOIN \"WorkContexts\" c ON c.\"Id\"=NEW.\"SourceWorkContextId\" JOIN \"AcceptedBatchReceipts\" b ON b.\"TaskId\"=a.\"TaskId\" AND b.\"BatchId\"=NEW.\"BatchId\" JOIN \"InspectionAttemptApplicabilities\" p ON p.\"PatrolTaskItemId\"=a.\"TaskItemId\" WHERE a.\"Id\"=NEW.\"AttachmentId\" AND a.\"TaskId\"=NEW.\"TaskId\" AND a.\"TaskItemId\"=NEW.\"TaskItemId\" AND a.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"InspectionAttemptId\"=a.\"InspectionAttemptId\" AND a.\"LineId\"=NEW.\"LineId\" AND a.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND a.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND a.\"ClientPhotoId\"=NEW.\"ClientPhotoId\" AND a.\"ExpectedSha256\"=NEW.\"Sha256\" AND a.\"SourceWorkContextId\"=NEW.\"PhotoSourceWorkContextId\" AND a.\"ContributorUserId\"=NEW.\"PhotoContributorUserId\" AND s.\"UploadedByUserId\"=NEW.\"PhotoUploadedByUserId\" AND s.\"UploadedPersonalSessionId\"=NEW.\"PhotoUploadPersonalSessionId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND b.\"DraftVersion\"=NEW.\"DraftVersion\"";
        const string current = "SELECT 1 FROM \"DraftAttachmentProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"AttachmentId\"=NEW.\"AttachmentId\"";
        const string predecessor = "SELECT 1 FROM \"DraftAttachmentProjections\" p JOIN \"DraftAttachmentOperations\" o ON o.\"TaskId\"=p.\"TaskId\" AND o.\"OperationId\"=p.\"LastOperationId\" WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"AttachmentId\"=NEW.\"AttachmentId\" AND p.\"LastOperationId\"=NEW.\"PredecessorOperationId\" AND o.\"ContentHash\"=NEW.\"PredecessorHash\" AND o.\"DraftVersion\" < NEW.\"DraftVersion\" AND p.\"IsAttached\"=(NEW.\"Kind\"='DetachAttachment')";
        const string ready = "SELECT 1 FROM \"AttachmentAvailabilities\" v WHERE v.\"AttachmentId\"=NEW.\"AttachmentId\" AND v.\"State\"='Ready' AND v.\"AcceptedAttachmentId\"=NEW.\"AttachmentId\"";
        const string removed = "SELECT 1 FROM \"ItemRemovalOperations\" r WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        const string projection = "SELECT 1 FROM \"DraftAttachmentOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"AttachmentId\"=NEW.\"AttachmentId\" AND NEW.\"IsAttached\"=(o.\"Kind\"='AssociateAttachment')";
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        void Guard(string function, string trigger, string table, string actions, string condition, string error, string extra = "") {
            if (pg) {
                m.Sql($"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; {extra} RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"{trigger}\" BEFORE {actions} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION {function}();");
            } else m.Sql($"CREATE TRIGGER \"{trigger}\" BEFORE {actions} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {condition}; {extra} END;");
        }
        const string prior = "SELECT 1 FROM \"SharedOperationIdentities\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\"";
        // Preserve the original table's own PK violation for existing sample/item duplicates.
        // Only a key owned solely by the new reference domain needs the new conflict guard.
        Guard("ipqc_index_prior_draft_key", "TR_SharedOperationIdentities_AllDomains", "SharedOperationIdentities", "INSERT", $"EXISTS ({exists}) AND NOT EXISTS ({prior})", "OperationIdentityConflict",
            $"INSERT INTO \"DraftOperationKeys\" (\"TaskId\",\"OperationId\",\"Domain\") SELECT NEW.\"TaskId\",NEW.\"OperationId\",NEW.\"Domain\" WHERE NOT EXISTS ({prior});");
        Guard("ipqc_draft_photo_scope", "TR_DraftPhoto_Scope", "DraftAttachmentOperations", "INSERT",
            $"NOT EXISTS ({scope}) OR (NEW.\"PredecessorOperationId\" IS NULL AND EXISTS ({current})) OR (NEW.\"PredecessorOperationId\" IS NOT NULL AND NOT EXISTS ({predecessor})) OR (NEW.\"Kind\"='AssociateAttachment' AND (NOT EXISTS ({ready}) OR EXISTS ({removed})))", "DraftAttachmentScopeConflict");
        Guard("ipqc_index_photo_draft_key", "TR_DraftPhoto_Identity", "DraftAttachmentOperations", "INSERT", $"EXISTS ({exists})", "OperationIdentityConflict",
            "INSERT INTO \"DraftOperationKeys\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'AttachmentReference');");
        foreach (var action in new[] { "INSERT", "UPDATE" }) {
            var suffix = action.ToLowerInvariant();
            var change = action == "INSERT" ? "" : " OR OLD.\"TaskId\"<>NEW.\"TaskId\" OR OLD.\"AttachmentId\"<>NEW.\"AttachmentId\" OR NOT EXISTS (SELECT 1 FROM \"DraftAttachmentOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"PredecessorOperationId\"=OLD.\"LastOperationId\")";
            Guard("ipqc_draft_photo_projection_" + suffix, "TR_DraftPhotoProjection_" + action, "DraftAttachmentProjections", action, $"NOT EXISTS ({projection}){change}", "DraftAttachmentProjectionConflict");
        }
        foreach (var table in new[] { "DraftOperationKeys", "DraftAttachmentOperations", "DraftAttachmentProjections" }) {
            var actions = table == "DraftAttachmentProjections" ? new[] { "DELETE" } : new[] { "UPDATE", "DELETE" };
            foreach (var action in actions) {
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
            }
        }
    }
}
