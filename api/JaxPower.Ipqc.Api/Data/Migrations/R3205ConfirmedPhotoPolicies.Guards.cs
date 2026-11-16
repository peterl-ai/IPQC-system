using Microsoft.EntityFrameworkCore.Migrations;

namespace JaxPower.Ipqc.Api.Data.Migrations;

public partial class R3205ConfirmedPhotoPolicies
{
    private void Protect(MigrationBuilder m)
    {
        var pg = ActiveProvider?.Contains("Npgsql") == true;
        void Guard(string name, string table, string action, string condition, string error) {
            if (pg) {
                m.Sql($"CREATE FUNCTION ipqc_{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION '{error}' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
                m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_{name}();");
            } else m.Sql($"CREATE TRIGGER \"TR_{name}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'{error}') WHERE {condition}; END;");
        }
        const string definitionScope = "SELECT 1 FROM \"PatrolStandardItems\" i WHERE i.\"Id\"=NEW.\"StandardItemId\" AND i.\"PatrolStandardId\"=NEW.\"StandardId\"";
        Guard("policy_definition_scope", "PhotoPolicyDefinitions", "INSERT", $"NOT EXISTS ({definitionScope})", "PhotoPolicyDefinitionScopeConflict");
        var equalPrior = pg ? "s.\"ConfirmationId\" IS NOT DISTINCT FROM NEW.\"ExpectedConfirmationId\"" : "s.\"ConfirmationId\" IS NEW.\"ExpectedConfirmationId\"";
        var confirmation = $"SELECT 1 FROM \"PhotoPolicyStates\" s JOIN \"PhotoPolicyDefinitions\" d ON d.\"Id\"=s.\"DefinitionId\" JOIN \"PatrolStandardItems\" i ON i.\"Id\"=s.\"StandardItemId\" JOIN \"ApplicationUsers\" a ON a.\"Id\"=NEW.\"ActorUserId\" WHERE s.\"StandardItemId\"=NEW.\"StandardItemId\" AND s.\"StandardId\"=NEW.\"StandardId\" AND s.\"DefinitionId\"=NEW.\"DefinitionId\" AND s.\"IsPresent\" AND d.\"StandardItemId\"=s.\"StandardItemId\" AND d.\"StandardId\"=s.\"StandardId\" AND i.\"PatrolStandardId\"=s.\"StandardId\" AND s.\"PolicyVersion\"<9223372036854775807 AND NEW.\"PolicyVersion\"=s.\"PolicyVersion\"+1 AND {equalPrior} AND a.\"Role\"='PQE' AND a.\"IsActive\" AND NOT a.\"MustChangePassword\"";
        Guard("policy_confirmation_scope", "PhotoPolicyConfirmations", "INSERT", $"NOT EXISTS ({confirmation})", "PhotoPolicyConfirmationScopeConflict");
        const string stateScope = "SELECT 1 FROM \"PhotoPolicyDefinitions\" d WHERE d.\"Id\"=NEW.\"DefinitionId\" AND d.\"StandardItemId\"=NEW.\"StandardItemId\" AND d.\"StandardId\"=NEW.\"StandardId\"";
        const string stateConfirmation = "SELECT 1 FROM \"PhotoPolicyConfirmations\" c WHERE c.\"Id\"=NEW.\"ConfirmationId\" AND c.\"StandardItemId\"=NEW.\"StandardItemId\" AND c.\"StandardId\"=NEW.\"StandardId\" AND c.\"DefinitionId\"=NEW.\"DefinitionId\" AND c.\"PolicyVersion\"=NEW.\"PolicyVersion\"";
        Guard("policy_state_insert", "PhotoPolicyStates", "INSERT", $"NOT EXISTS ({stateScope}) OR NEW.\"ConfirmationId\" IS NOT NULL OR NEW.\"PolicyVersion\"<>0", "PhotoPolicyStateConflict");
        const string nextDefinition = "SELECT 1 FROM \"PhotoPolicyDefinitions\" n JOIN \"PhotoPolicyDefinitions\" o ON o.\"Id\"=OLD.\"DefinitionId\" WHERE n.\"Id\"=NEW.\"DefinitionId\" AND n.\"Version\"=o.\"Version\"+1";
        const string predecessor = "SELECT 1 FROM \"PhotoPolicyConfirmations\" c WHERE c.\"Id\"=NEW.\"ConfirmationId\" AND ";
        var statePrior = pg ? "c.\"ExpectedConfirmationId\" IS NOT DISTINCT FROM OLD.\"ConfirmationId\"" : "c.\"ExpectedConfirmationId\" IS OLD.\"ConfirmationId\"";
        var change = $"OLD.\"StandardId\"<>NEW.\"StandardId\" OR OLD.\"StandardItemId\"<>NEW.\"StandardItemId\" OR NOT EXISTS ({stateScope}) OR (NEW.\"DefinitionId\"<>OLD.\"DefinitionId\" AND (NEW.\"ConfirmationId\" IS NOT NULL OR NEW.\"PolicyVersion\"<>OLD.\"PolicyVersion\" OR NOT EXISTS ({nextDefinition}))) OR (NEW.\"DefinitionId\"=OLD.\"DefinitionId\" AND ((NEW.\"ConfirmationId\" IS NULL AND NEW.\"PolicyVersion\"<>OLD.\"PolicyVersion\") OR (NEW.\"ConfirmationId\" IS NOT NULL AND (NOT EXISTS ({stateConfirmation}) OR NEW.\"PolicyVersion\"<>OLD.\"PolicyVersion\"+1 OR NOT EXISTS ({predecessor}{statePrior})))))";
        Guard("policy_state_update", "PhotoPolicyStates", "UPDATE", change, "PhotoPolicyStateConflict");
        const string frozenScope = "SELECT 1 FROM \"PatrolTaskItems\" i JOIN \"PatrolTasks\" t ON t.\"Id\"=i.\"PatrolTaskId\" JOIN \"PhotoPolicyConfirmations\" c ON c.\"Id\"=NEW.\"ConfirmationId\" JOIN \"PhotoPolicyStates\" s ON s.\"StandardItemId\"=c.\"StandardItemId\" WHERE i.\"Id\"=NEW.\"TaskItemId\" AND t.\"Id\"=NEW.\"TaskId\" AND t.\"WorkflowVersion\"=2 AND t.\"PlanningSnapshotVersion\"=1 AND i.\"SourcePatrolStandardItemId\"=c.\"StandardItemId\" AND t.\"PatrolStandardId\"=c.\"StandardId\" AND s.\"IsPresent\" AND s.\"ConfirmationId\"=c.\"Id\" AND s.\"DefinitionId\"=c.\"DefinitionId\"";
        Guard("policy_frozen_scope", "FrozenTaskPhotoPolicies", "INSERT", $"NOT EXISTS ({frozenScope})", "FrozenPhotoPolicyScopeConflict");
        foreach (var table in new[] { "PhotoPolicyDefinitions", "PhotoPolicyConfirmations", "PhotoPolicyStates", "FrozenTaskPhotoPolicies" })
            foreach (var action in table == "PhotoPolicyStates" ? new[] { "DELETE" } : new[] { "UPDATE", "DELETE" })
                if (pg) m.Sql($"CREATE TRIGGER \"TR_{table}_{action}\" BEFORE {action} ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                else m.Sql($"CREATE TRIGGER \"TR_{table}_{action}\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted photo policy history is immutable'); END;");
    }
}
