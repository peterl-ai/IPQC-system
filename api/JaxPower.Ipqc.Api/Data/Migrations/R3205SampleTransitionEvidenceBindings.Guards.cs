using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205SampleTransitionEvidenceBindings
{
    private void Protect(MigrationBuilder m)
    {
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        void Guard(string name, string table, string action, string condition, string error, string extra = "")
        {
            if (pg) {
                m.Sql($"CREATE FUNCTION ipqc_{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; {extra} RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_{name}();");
            } else m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {condition}; {extra} END;");
        }
        Guard("binding_identity", "SampleEvidenceOperations", "INSERT",
            "EXISTS (SELECT 1 FROM \"AcceptedOperationIdentities\" WHERE \"TaskId\"=NEW.\"TaskId\" AND \"OperationId\"=NEW.\"OperationId\")", "OperationIdentityConflict",
            "INSERT INTO \"AcceptedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'SampleTransitionEvidence');");

        const string scope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"PatrolTaskItems\" i ON i.\"PatrolTaskId\"=t.\"Id\" JOIN \"InspectionAttemptApplicabilities\" p ON p.\"PatrolTaskItemId\"=i.\"Id\" JOIN \"AcceptedBatchReceipts\" b ON b.\"TaskId\"=t.\"Id\" AND b.\"BatchId\"=NEW.\"BatchId\" JOIN \"SharedDrafts\" d ON d.\"TaskId\"=t.\"Id\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND i.\"Id\"=NEW.\"TaskItemId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND b.\"DraftVersion\"=NEW.\"DraftVersion\" AND d.\"DraftVersion\"=NEW.\"DraftVersion\"";
        const string projection = "SELECT 1 FROM \"SampleEvidenceProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"BindingId\"=NEW.\"BindingId\"";
        const string predecessor = "SELECT 1 FROM \"SampleEvidenceProjections\" p JOIN \"SampleEvidenceOperations\" o ON o.\"TaskId\"=p.\"TaskId\" AND o.\"OperationId\"=p.\"LastOperationId\" WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"BindingId\"=NEW.\"BindingId\" AND p.\"IsActive\" AND o.\"OperationId\"=NEW.\"PredecessorOperationId\" AND o.\"ContentHash\"=NEW.\"PredecessorHash\" AND o.\"SampleOperationId\"=NEW.\"SampleOperationId\" AND o.\"SampleHash\"=NEW.\"SampleHash\" AND o.\"TaskItemId\"=NEW.\"TaskItemId\" AND o.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND o.\"DraftVersion\"<NEW.\"DraftVersion\"";
        const string root = "SELECT 1 FROM \"SampleEvidenceOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"BindingId\" AND o.\"Kind\"='BindSampleEvidence'";
        const string removed = "SELECT 1 FROM \"ItemRemovalOperations\" r WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        const string unable = "SELECT 1 FROM \"UnableToMeasureProjections\" p WHERE p.\"TaskId\"=NEW.\"TaskId\" AND p.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND p.\"IsActive\"";
        const string photo = "SELECT 1 FROM \"DraftAttachmentOperations\" r JOIN \"DraftAttachmentProjections\" p ON p.\"TaskId\"=r.\"TaskId\" AND p.\"AttachmentId\"=r.\"AttachmentId\" JOIN \"AttachmentAvailabilities\" v ON v.\"AttachmentId\"=r.\"AttachmentId\" JOIN \"InspectionAttachments\" a ON a.\"Id\"=r.\"AttachmentId\" WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"OperationId\"=NEW.\"PhotoReferenceOperationId\" AND r.\"ContentHash\"=NEW.\"PhotoReferenceHash\" AND r.\"AttachmentId\"=NEW.\"AttachmentId\" AND r.\"Kind\"='AssociateAttachment' AND r.\"TaskItemId\"=NEW.\"TaskItemId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND r.\"LineId\"=NEW.\"LineId\" AND r.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND r.\"WorkflowVersion\"=2 AND r.\"DraftVersion\"<NEW.\"DraftVersion\" AND p.\"LastOperationId\"=r.\"OperationId\" AND p.\"IsAttached\" AND v.\"State\"='Ready' AND v.\"AcceptedAttachmentId\"=r.\"AttachmentId\" AND a.\"TaskId\"=NEW.\"TaskId\" AND a.\"TaskItemId\"=NEW.\"TaskItemId\" AND a.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
        string Judgment(string alias) => pg ? $"({alias}.\"ReceiptJson\"::jsonb #>> '{{value,judgment}}')" : $"json_extract({alias}.\"ReceiptJson\",'$.value.judgment')";
        var to = Judgment("s"); var from = Judgment("v");
        var transition = $"SELECT 1 FROM \"ContributionOperations\" s WHERE s.\"TaskId\"=NEW.\"TaskId\" AND s.\"OperationId\"=NEW.\"SampleOperationId\" AND s.\"ContentHash\"=NEW.\"SampleHash\" AND s.\"SampleId\"=NEW.\"SampleId\" AND s.\"TaskItemId\"=NEW.\"TaskItemId\" AND s.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\" AND s.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND s.\"LineId\"=NEW.\"LineId\" AND s.\"WorkflowVersion\"=2 AND s.\"CanonicalizationVersion\"=1 AND s.\"DraftVersion\"<NEW.\"DraftVersion\" AND ((s.\"Kind\"='CaptureSample' AND {to}='NG' AND NEW.\"TransitionKind\"='InitialNGCapture' AND NOT EXISTS (SELECT 1 FROM \"ContributionDependencies\" d WHERE d.\"TaskId\"=s.\"TaskId\" AND d.\"OperationId\"=s.\"OperationId\")) OR (s.\"Kind\"='CorrectSample' AND (SELECT count(*) FROM \"ContributionDependencies\" d WHERE d.\"TaskId\"=s.\"TaskId\" AND d.\"OperationId\"=s.\"OperationId\")=1 AND EXISTS (SELECT 1 FROM \"ContributionDependencies\" d JOIN \"ContributionOperations\" v ON v.\"TaskId\"=d.\"TaskId\" AND v.\"OperationId\"=d.\"DependencyId\" AND v.\"ContentHash\"=d.\"ContentHash\" WHERE d.\"TaskId\"=s.\"TaskId\" AND d.\"OperationId\"=s.\"OperationId\" AND v.\"SampleId\"=s.\"SampleId\" AND v.\"TaskItemId\"=s.\"TaskItemId\" AND v.\"InspectionAttemptId\"=s.\"InspectionAttemptId\" AND (({from}='NG' AND {to}='OK' AND NEW.\"TransitionKind\"='NGToOKCorrection') OR ({from}='NG' AND {to}='NG' AND NEW.\"TransitionKind\"='NGToNGCorrection') OR ({from}='OK' AND {to}='NG' AND NEW.\"TransitionKind\"='OKToNGCorrection')))))";
        Guard("binding_scope", "SampleEvidenceOperations", "INSERT",
            $"NOT EXISTS ({scope}) OR NOT EXISTS ({transition}) OR (NEW.\"Kind\"='BindSampleEvidence' AND EXISTS ({projection})) OR (NEW.\"Kind\"<>'BindSampleEvidence' AND (NOT EXISTS ({root}) OR NOT EXISTS ({predecessor}))) OR (NEW.\"Kind\"<>'WithdrawSampleEvidence' AND (EXISTS ({removed}) OR EXISTS ({unable}) OR NOT EXISTS ({photo})))", "BindingScopeConflict");
        const string pointer = "SELECT 1 FROM \"SampleEvidenceOperations\" o WHERE o.\"TaskId\"=NEW.\"TaskId\" AND o.\"OperationId\"=NEW.\"LastOperationId\" AND o.\"BindingId\"=NEW.\"BindingId\" AND NEW.\"IsActive\"=(o.\"Kind\"<>'WithdrawSampleEvidence')";
        Guard("binding_projection_insert", "SampleEvidenceProjections", "INSERT", $"NOT EXISTS ({pointer} AND o.\"Kind\"='BindSampleEvidence')", "BindingProjectionConflict");
        Guard("binding_projection_update", "SampleEvidenceProjections", "UPDATE", $"OLD.\"TaskId\"<>NEW.\"TaskId\" OR OLD.\"BindingId\"<>NEW.\"BindingId\" OR NOT OLD.\"IsActive\" OR NOT EXISTS ({pointer} AND o.\"PredecessorOperationId\"=OLD.\"LastOperationId\")", "BindingProjectionConflict");
        foreach (var table in new[] { "SampleEvidenceOperations", "SampleEvidenceProjections" })
            foreach (var action in table == "SampleEvidenceProjections" ? new[] { "DELETE" } : new[] { "UPDATE", "DELETE" })
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
    }
}
