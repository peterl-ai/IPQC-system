using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3206ProtectedPhotoReady
{
    private void Protect(MigrationBuilder m)
    {
        const string scope="SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"PatrolTaskItems\" i ON i.\"PatrolTaskId\"=t.\"Id\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND i.\"Id\"=NEW.\"TaskItemId\"";
        const string accepted="SELECT 1 FROM \"InspectionAttachments\" a JOIN \"AttachmentUploads\" u ON u.\"AttachmentId\"=a.\"Id\" WHERE a.\"Id\"=NEW.\"AttachmentId\" AND u.\"Id\"=NEW.\"UploadId\" AND u.\"StoredAtUtc\"=NEW.\"StoredAtUtc\" AND a.\"ExpectedBytes\"=NEW.\"ByteCount\" AND a.\"ExpectedSha256\"=NEW.\"Sha256\" AND a.\"DeclaredContentType\"=NEW.\"ContentType\"";
        const string uploadSame="OLD.\"Id\"=NEW.\"Id\" AND OLD.\"AttachmentId\"=NEW.\"AttachmentId\" AND OLD.\"StageKey\"=NEW.\"StageKey\" AND OLD.\"StartedByUserId\"=NEW.\"StartedByUserId\" AND OLD.\"StartedPersonalSessionId\"=NEW.\"StartedPersonalSessionId\" AND OLD.\"StartedAtUtc\"=NEW.\"StartedAtUtc\" AND OLD.\"StoredAtUtc\" IS NULL AND NEW.\"StoredAtUtc\" IS NOT NULL";
        if(ActiveProvider?.Contains("Npgsql")==true) {
            foreach(var table in new[]{"InspectionAttachments","AttachmentAcceptances","AttachmentEvents"})
                m.Sql($"CREATE TRIGGER \"TR_{table}_Immutable\" BEFORE UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
            m.Sql($"CREATE FUNCTION ipqc_photo_scope() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NOT EXISTS ({scope}) THEN RAISE EXCEPTION 'Photo scope mismatch' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
            m.Sql("CREATE TRIGGER \"TR_PhotoScope\" BEFORE INSERT ON \"InspectionAttachments\" FOR EACH ROW EXECUTE FUNCTION ipqc_photo_scope();");
            m.Sql($"CREATE FUNCTION ipqc_photo_acceptance() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NOT EXISTS ({accepted}) THEN RAISE EXCEPTION 'Photo acceptance mismatch' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
            m.Sql("CREATE TRIGGER \"TR_PhotoAcceptance\" BEFORE INSERT ON \"AttachmentAcceptances\" FOR EACH ROW EXECUTE FUNCTION ipqc_photo_acceptance();");
            m.Sql($"CREATE FUNCTION ipqc_photo_upload_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Upload history is immutable' USING ERRCODE='23514'; END IF; IF NOT ({uploadSame}) THEN RAISE EXCEPTION 'Upload history is immutable' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
            m.Sql("CREATE TRIGGER \"TR_PhotoUploadImmutable\" BEFORE UPDATE OR DELETE ON \"AttachmentUploads\" FOR EACH ROW EXECUTE FUNCTION ipqc_photo_upload_guard();");
        } else {
            foreach(var table in new[]{"InspectionAttachments","AttachmentAcceptances","AttachmentEvents"}) foreach(var action in new[]{"UPDATE","DELETE"})
                m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Photo history is immutable'); END;");
            m.Sql($"CREATE TRIGGER \"TR_PhotoScope\" BEFORE INSERT ON \"InspectionAttachments\" BEGIN SELECT RAISE(ABORT,'Photo scope mismatch') WHERE NOT EXISTS ({scope}); END;");
            m.Sql($"CREATE TRIGGER \"TR_PhotoAcceptance\" BEFORE INSERT ON \"AttachmentAcceptances\" BEGIN SELECT RAISE(ABORT,'Photo acceptance mismatch') WHERE NOT EXISTS ({accepted}); END;");
            m.Sql($"CREATE TRIGGER \"TR_PhotoUpload_UPDATE\" BEFORE UPDATE ON \"AttachmentUploads\" BEGIN SELECT RAISE(ABORT,'Upload history is immutable') WHERE NOT ({uploadSame}); END;");
            m.Sql("CREATE TRIGGER \"TR_PhotoUpload_DELETE\" BEFORE DELETE ON \"AttachmentUploads\" BEGIN SELECT RAISE(ABORT,'Upload history is immutable'); END;");
        }
    }
}
