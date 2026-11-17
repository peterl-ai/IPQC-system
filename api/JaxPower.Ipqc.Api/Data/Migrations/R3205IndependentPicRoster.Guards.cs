using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205IndependentPicRoster
{
    private void Protect(MigrationBuilder m) {
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        const string prior = "SELECT 1 FROM \"PicRosterRevisions\" p WHERE p.\"PicId\"=NEW.\"PicId\" AND p.\"Revision\"=NEW.\"Revision\"-1 AND ((NEW.\"Action\"='Updated' AND p.\"IsActive\"=NEW.\"IsActive\" AND (p.\"Name\"<>NEW.\"Name\" OR p.\"Department\"<>NEW.\"Department\")) OR (NEW.\"Action\"='Activated' AND NOT p.\"IsActive\" AND NEW.\"IsActive\" AND p.\"Name\"=NEW.\"Name\" AND p.\"Department\"=NEW.\"Department\") OR (NEW.\"Action\"='Deactivated' AND p.\"IsActive\" AND NOT NEW.\"IsActive\" AND p.\"Name\"=NEW.\"Name\" AND p.\"Department\"=NEW.\"Department\"))";
        var invalid = "NOT EXISTS (SELECT 1 FROM \"ApplicationUsers\" a WHERE a.\"Id\"=NEW.\"ActorUserId\" AND a.\"IsActive\" AND a.\"Role\"='Admin' AND NOT a.\"MustChangePassword\" AND a.\"Username\"=NEW.\"ActorUsername\") OR NEW.\"Revision\"<>(SELECT COALESCE(MAX(r.\"Revision\"),0)+1 FROM \"PicRosterRevisions\" r WHERE r.\"PicId\"=NEW.\"PicId\") OR (NEW.\"Revision\"=1 AND (NEW.\"Action\"<>'Created' OR NOT EXISTS (SELECT 1 FROM \"PicRosterEntries\" i WHERE i.\"PicId\"=NEW.\"PicId\" AND i.\"CreatedAtUtc\"=NEW.\"OccurredAtUtc\"))) OR (NEW.\"Revision\">1 AND NOT EXISTS (" + prior + "))";
        if (pg) {
            m.Sql($"CREATE FUNCTION ipqc_pic_revision_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {invalid} THEN RAISE EXCEPTION 'PicRevisionConflict' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
            m.Sql("CREATE TRIGGER \"TR_pic_revision\" BEFORE INSERT ON \"PicRosterRevisions\" FOR EACH ROW EXECUTE FUNCTION ipqc_pic_revision_guard();");
        } else m.Sql($"CREATE TRIGGER \"TR_pic_revision\" BEFORE INSERT ON \"PicRosterRevisions\" BEGIN SELECT RAISE(ABORT,'PicRevisionConflict') WHERE {invalid}; END;");
        foreach (var table in new[] { "PicRosterEntries", "PicRosterRevisions" })
            foreach (var action in new[] { "UPDATE", "DELETE" })
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'PIC history is immutable'); END;");
    }
}
