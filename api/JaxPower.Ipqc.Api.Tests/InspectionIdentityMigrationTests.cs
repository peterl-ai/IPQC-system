using System.Text.Json;
using JaxPower.Ipqc.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class InspectionIdentityMigrationTests
{
    public const string Previous = "20261111214948_R3204WorkContextFoundation";
    [Theory][InlineData(false)][InlineData(true)]
    public void ID04_Both_models_and_additive_native_schema(bool postgres)
    {
        var o = new DbContextOptionsBuilder<IpqcDbContext>();
        if (postgres) o.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else o.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(o.Options); Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(Previous);
        Assert.Contains("CREATE TABLE \"InspectionAttempts\"", sql); Assert.Contains("CREATE TABLE \"InspectionAttemptApplicabilities\"", sql);
        Assert.Contains("FOREIGN KEY (\"InspectionAttemptId\", \"PatrolTaskItemId\")", sql);
        Assert.Contains("WHERE \"Kind\" = 'Initial'", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("UPDATE \"", sql);
        if (postgres) { Assert.Contains("\"Id\" uuid", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), Previous));
    }
    [Fact] public async Task ID04_Populated_SQLite_preserves_all_old_facts_and_no_backfill()
    { await using var f = await PersonalSessionFixture.CreateAsync(migration: Previous); await Upgrade(f); }

    internal static async Task Upgrade(PersonalSessionFixture f)
    {
        var existing = await InspectionIdentityTests.Existing(f); var auth = await f.Login(); var legacy = await f.Task(workflow: JaxPower.Ipqc.Api.Domain.WorkflowVersion.LegacyPersonalV1);
        await f.Edit(db => {
            var c = new JaxPower.Ipqc.Api.Domain.WorkContext { Id = Guid.NewGuid(), ProofSecretHash = new string('0',64),
                EnvironmentId = PersonalSessionFixture.Environment, UserId = TestAccounts.Ipqa1Id,
                IssuedPersonalSessionId = auth.PersonalSessionId!.Value, IssuedAuthVersion = 1, TaskId = existing,
                LineId = f.Line1, IssuedAtUtc = f.Clock.Now.UtcDateTime, CaptureUntilUtc = f.Clock.Now.AddHours(72).UtcDateTime,
                PersonalSessionExpiresAtUtc = f.Clock.Now.AddDays(14).UtcDateTime };
            db.WorkContexts.Add(c); new JaxPower.Ipqc.Api.Services.WorkContextSecurity(db,f.Clock).Audit(c,TestAccounts.Ipqa1Id,"dev-ipqa-1","ContextIssued");
            return Task.CompletedTask;
        });
        await f.Edit(async db => { (await db.PatrolTaskItems.FirstAsync(x => x.PatrolTaskId == legacy)).Remarks = "Synthetic preserved historical answer"; });
        var before = await OldFacts(f);
        await f.Edit(async db => await db.Database.MigrateAsync());
        Assert.Equal(before, await OldFacts(f)); Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.InspectionAttemptApplicabilities.CountAsync()));
        var client = f.Client((await f.Login()).AccessToken);
        var read = await client.GetAsync(InspectionIdentityTests.Root(existing)); Assert.True(read.IsSuccessStatusCode, await read.Content.ReadAsStringAsync());
        Assert.Contains("Uninitialized", await read.Content.ReadAsStringAsync());
        await WorkContextFixture.Issue(f, existing, await f.Login());
        Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync()));
        if (f.PostgresRoot is null) await f.Read(async db => {
            await db.Database.OpenConnectionAsync(); await using var c = db.Database.GetDbConnection().CreateCommand(); c.CommandText = "PRAGMA foreign_key_check";
            await using var r = await c.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true;
        });
    }

    [Theory][InlineData("table")][InlineData("history")]
    public async Task ID04_Additive_upgrade_failure_is_rollback_safe(string point)
    { await using var f = await PersonalSessionFixture.CreateAsync(migration: Previous); await UpgradeRollback(f,point); }
    internal static async Task UpgradeRollback(PersonalSessionFixture f,string point)
    {
        await InspectionIdentityTests.Existing(f); var before = await OldFacts(f);
        f.Fault.Arm(point == "table" ? "CREATE TABLE \"InspectionAttemptApplicabilities\"" : "INSERT INTO \"__EFMigrationsHistory\"",true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Edit(async db=>await db.Database.MigrateAsync()));
        Assert.Equal(before,await OldFacts(f));
        Assert.DoesNotContain(await f.Read(async db=>(await db.Database.GetAppliedMigrationsAsync()).ToArray()),x=>x.Contains("R3205",StringComparison.Ordinal));
        await f.Edit(async db=>await db.Database.MigrateAsync());
        Assert.Equal(before,await OldFacts(f)); Assert.Equal(0,await f.Read(db=>db.InspectionAttempts.CountAsync()));
    }

    // Fingerprint every preexisting row, column, FK and index, including session/context history.
    internal static Task<string> OldFacts(PersonalSessionFixture f, bool includeIdentities = false, bool includeDrafts = false, bool includeRemoved = false, bool includePhotos = false, bool includeReferences = false) => f.Read(async db => {
        await db.Database.OpenConnectionAsync(); var connection = db.Database.GetDbConnection();
        async Task<List<string>> Query(string sql) {
            await using var c = connection.CreateCommand(); c.CommandText = sql; await using var r = await c.ExecuteReaderAsync(); var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? null : r.GetValue(i)).ToArray())); return rows;
        }
        var excluded = "'__EFMigrationsHistory','__EFMigrationsLock','AcceptedOperationIdentities','UnableToMeasureOperations','UnableToMeasureProjections','SampleEvidenceOperations','SampleEvidenceProjections'" +
            (includeReferences ? "" : ",'DraftOperationKeys','DraftAttachmentOperations','DraftAttachmentProjections'") +
            (includePhotos ? "" : ",'InspectionAttachments','AttachmentAvailabilities','AttachmentUploads','AttachmentAcceptances','AttachmentEvents'") +
            (includeRemoved ? "" : ",'ItemRemovalOperations','SharedOperationIdentities'") +
            (includeDrafts ? "" : ",'SharedDrafts','SharedSamples','SharedSampleProjections','ContributionOperations','ContributionDependencies','AcceptedBatchReceipts','SharedAcceptanceAudits'") +
            (includeIdentities ? "" : ",'InspectionAttempts','InspectionAttemptApplicabilities'");
        var tables = new List<string>(); await using (var c = connection.CreateCommand()) {
            c.CommandText = db.Database.IsNpgsql() ? $"SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ({excluded}) ORDER BY tablename" :
                $"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ({excluded}) ORDER BY name";
            await using var r = await c.ExecuteReaderAsync(); while (await r.ReadAsync()) tables.Add(r.GetString(0));
        }
        var data = new SortedDictionary<string, List<string>>();
        foreach (var table in tables) {
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            var rows = await Query($"SELECT * FROM {quoted}"); data[table] = rows.Order(StringComparer.Ordinal).ToList();
        }
        if (db.Database.IsNpgsql()) {
            data["columns"] = await Query($"SELECT row_to_json(c)::text FROM information_schema.columns c WHERE table_schema='public' AND table_name NOT IN ({excluded}) ORDER BY table_name,ordinal_position");
            data["indexes"] = await Query($"SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND tablename NOT IN ({excluded}) ORDER BY tablename,indexname");
            data["constraints"] = await Query($"SELECT conrelid::regclass::text,conname,pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='public'::regnamespace AND replace(conrelid::regclass::text,'\"','') NOT IN ({excluded}) ORDER BY conrelid::regclass::text,conname");
        // The new additive ID registration trigger is checked separately by Removed migration tests.
        } else data["schema"] = await Query($"SELECT type,name,tbl_name,sql FROM sqlite_master WHERE tbl_name NOT IN ({excluded}) AND name NOT LIKE 'sqlite_%' AND name NOT IN ('TR_ContributionOperations_Identity','TR_SharedOperationIdentities_AllDomains','TR_DraftOperationKeys_AllDomains','TR_ContributionOperations_UnableGuard','TR_ItemRemovalOperations_UnableGuard') ORDER BY type,name");
        return JsonSerializer.Serialize(data);
    });
}
