using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.NgPicSelectionFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class NgPicSelectionMigrationTests
{
    [Theory][InlineData(false)][InlineData(true)] public void Provider_models_match_snapshot_and_use_only_three_additive_native_tables(bool pg) {
        var options = new DbContextOptionsBuilder<IpqcDbContext>(); if (pg) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options); Assert.False(db.Database.HasPendingModelChanges()); var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current);
        Assert.Equal(3, Regex.Matches(sql, "CREATE TABLE ").Count); Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql);
        foreach (var expected in new[] { "NgPicSelectionOperations", "NgPicSelectionMembers", "NgPicSelectionProjections", "TR_ng_pic_scope", "TR_ng_pic_member", "ON DELETE RESTRICT", "NgPicSnapshotConflict" }) Assert.Contains(expected, sql);
        if (pg) { Assert.Contains("\"PicId\" uuid", sql); Assert.Contains("\"RosterRevision\" bigint", sql); Assert.Contains("timestamp with time zone", sql); }
        foreach (var entity in db.Model.GetEntityTypes().Where(x => x.GetTableName()!.StartsWith("NgPic", StringComparison.Ordinal))) {
            Assert.All(entity.GetIndexes(), i => Assert.True(i.GetDatabaseName()!.Length <= 63));
            Assert.All(entity.GetForeignKeys(), fk => Assert.True(fk.GetConstraintName()!.Length <= 63));
        }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(Current, Previous));
    }
    private static async Task<string> Prior(PhotoFixture p) {
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        var b = await SampleEvidenceTests.Setup(p); await SampleEvidenceTests.Accept(b, SampleEvidenceTests.Input(b));
        await BasicInformationFixture.Accept(b.S, BasicInformationFixture.Input(b.S, 3));
        var client = p.F.Client((await p.F.Login("admin")).AccessToken); var created = (await PicRosterTests.Accepted(await client.PostAsJsonAsync(PicRosterTests.Root, PicRosterTests.Input()))).Receipt;
        await PicRosterTests.Accepted(await client.PutAsJsonAsync(PicRosterTests.Root + "/" + created.PicId, new PicEditInput(Guid.NewGuid(), 1, "Historical renamed PIC", "Historical department")));
        _ = await p.F.Task(workflow: JaxPower.Ipqc.Api.Domain.WorkflowVersion.LegacyPersonalV1);
        return await PicRosterTests.Facts(p.F);
    }
    [Fact] public async Task Real_previous_populated_upgrade_preserves_all_old_facts_and_roster_revisions() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    internal static async Task Upgrade(PhotoFixture p) {
        var before = await Prior(p); await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await PicRosterTests.Facts(p.F));
        Assert.Equal(Current, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        Assert.Equal(0, await p.F.Read(db => db.NgPicSelectionOperations.CountAsync())); Assert.Equal(2, await p.F.Read(db => db.PicRosterRevisions.CountAsync()));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => { await db.Database.OpenConnectionAsync(); await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandText = "PRAGMA foreign_key_check"; await using var r = await q.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true; });
    }
    [Theory][InlineData("CREATE TABLE \"NgPicSelectionMembers\"")][InlineData("CREATE TRIGGER \"TR_ng_pic_scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task DDL_and_migration_history_failure_is_transactional(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await MigrationRollback(p, point); }
    internal static async Task MigrationRollback(PhotoFixture p, string point) {
        var before = await Prior(p); p.F.Fault.Arm(point, true); await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync()));
        Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully); Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        Assert.Equal(before, await PicRosterTests.Facts(p.F)); await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await PicRosterTests.Facts(p.F));
    }
    [Theory][InlineData("cas", false)][InlineData("receipt", false)][InlineData("operation", false)][InlineData("member", false)][InlineData("projection", false)][InlineData("audit", false)][InlineData("commit", false)]
    [InlineData("projection", true)][InlineData("audit", true)][InlineData("commit", true)]
    public async Task Every_acceptance_stage_rolls_back_before_a_real_retry(string point, bool correction) { await using var f = await PersonalSessionFixture.CreateAsync(); await AcceptanceRollback(f, point, correction); }
    internal static async Task AcceptanceRollback(PersonalSessionFixture f, string point, bool correction) {
        var u = await Setup(f); var first = correction ? (await Accept(u, Input(u))).Operation : null; var input = Input(u, correction ? 2 : 1, previous: first); var before = await Facts(f);
        if (point == "commit") f.CommitFault.Armed = true;
        else f.Fault.Arm(point switch { "cas" => "UPDATE \"SharedDrafts\"", "receipt" => "INSERT INTO \"AcceptedBatchReceipts\"", "operation" => "INSERT INTO \"NgPicSelectionOperations\"", "member" => "INSERT INTO \"NgPicSelectionMembers\"", "projection" => (correction ? "UPDATE" : "INSERT INTO") + " \"NgPicSelectionProjections\"", _ => "INSERT INTO \"SharedAcceptanceAudits\"" }, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(Url(u.S), input)).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await Facts(f)); Assert.False((await Accept(u, input)).ReplayOnly);
    }
    [Theory][InlineData("actor")][InlineData("scope")][InlineData("basis")][InlineData("missing-basis")][InlineData("snapshot")][InlineData("revision")]
    public async Task Native_guards_reject_tampered_actual_acceptance_not_only_invalid_requests(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Tamper(f, kind); }
    internal static async Task Tamper(PersonalSessionFixture f, string kind) {
        var u = await Setup(f); var before = await Facts(f); Exception? native = null; var injected = false;
        f.Fault.ObserveFailure = (_, e) => native = e.GetBaseException();
        f.Fault.ObserveAsync = (command, _) => {
            // Keep the operation and common receipt mutually consistent, so an empty/missing
            // NG basis must fail the genuine-NG predicate, not merely receipt equality.
            if (kind is "basis" or "missing-basis" && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal))
                foreach (DbParameter p in command.Parameters) if (p.Value is string text && text.StartsWith('{')) {
                    var node = System.Text.Json.Nodes.JsonNode.Parse(text)!;
                    var basis = kind == "missing-basis" ? "{}" : "{\"kind\":\"OrdinaryNG\",\"references\":[]}";
                    if (node["picSelectionOperations"] is System.Text.Json.Nodes.JsonArray a) { a[0]!["ngObservation"] = System.Text.Json.Nodes.JsonNode.Parse(basis); p.Value = node.ToJsonString(); }
                    else if (node["ngObservation"] is not null) { node["ngObservation"] = System.Text.Json.Nodes.JsonNode.Parse(basis); p.Value = node.ToJsonString(); }
                }
            var table = kind is "snapshot" or "revision" ? "NgPicSelectionMembers" : "NgPicSelectionOperations";
            var m = Regex.Match(command.CommandText, "INSERT INTO \"" + table + "\" \\((?<columns>[^)]+)\\)\\s+VALUES \\((?<values>[^)]+)\\)");
            if (!injected && m.Success) {
                var columns = m.Groups["columns"].Value.Split(',').Select(x => x.Trim().Trim('"')).ToArray(); var parameters = m.Groups["values"].Value.Split(',').Select(x => x.Trim().TrimStart('@')).ToArray();
                var column = kind switch { "actor" => "ActorUserId", "scope" => "InspectionAttemptId", "basis" or "missing-basis" => "NgBasisJson", "snapshot" => "Name", _ => "RosterRevision" };
                var parameter = command.Parameters.Cast<DbParameter>().Single(p => p.ParameterName.TrimStart('@') == parameters[Array.IndexOf(columns, column)]);
                parameter.Value = kind switch { "actor" => TestAccounts.PqeId, "scope" => Guid.NewGuid(), "basis" => "{\"kind\":\"OrdinaryNG\",\"references\":[]}", "missing-basis" => "{}", "snapshot" => "Client invented snapshot", _ => 77L }; injected = true;
            } return Task.CompletedTask;
        };
        try {
            Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u))).StatusCode); Assert.True(injected); Assert.NotNull(native);
            var expected = kind is "snapshot" or "revision" ? "NgPicSnapshotConflict" : "NgPicScopeConflict";
            if (f.PostgresRoot is not null) { var pg = Assert.IsType<Npgsql.PostgresException>(native); Assert.Equal("23514", pg.SqlState); Assert.Equal(expected, pg.MessageText); }
            else { var sq = Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(native); Assert.Equal(1811, sq.SqliteExtendedErrorCode); Assert.Contains(expected, sq.Message); }
        } finally { f.Fault.ObserveAsync = null; f.Fault.ObserveFailure = null; }
        Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Native_and_EF_history_immutability_and_projection_rewind_are_enforced() { await using var f = await PersonalSessionFixture.CreateAsync(); await Integrity(f); }
    internal static async Task Integrity(PersonalSessionFixture f) {
        var u = await Setup(f); var first = (await Accept(u, Input(u))).Operation; await Accept(u, Input(u, 2, [], first)); var before = await Facts(f);
        foreach (var table in new[] { "NgPicSelectionOperations", "NgPicSelectionMembers", "NgPicSelectionProjections" }) {
            var sql = $"DELETE FROM \"{table}\"";
            var ex = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlRawAsync(sql))); Assert.NotNull(ex); Assert.Contains("immutable", ex.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var table in new[] { "NgPicSelectionOperations", "NgPicSelectionMembers" }) {
            var sql = $"UPDATE \"{table}\" SET \"OperationId\"=\"OperationId\"";
            var ex = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlRawAsync(sql))); Assert.NotNull(ex); Assert.Contains("immutable", ex.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
        }
        var rewind = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"NgPicSelectionProjections\" SET \"LastOperationId\"={first.OperationId}")));
        Assert.NotNull(rewind); Assert.Contains("NgPicProjectionConflict", rewind.GetBaseException().Message);
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.NgPicSelectionMembers.FirstAsync()).Name = "Rewrite")); Assert.Contains("immutable", tracked.Message);
        Assert.Equal(before, await Facts(f));
    }
}
