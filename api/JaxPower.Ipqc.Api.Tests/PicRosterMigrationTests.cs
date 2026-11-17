using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.PicRosterTests;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PicRosterMigrationTests
{
    internal const string Previous = "20261116221536_R3205SharedTaskBasicInformationDraft";
    internal const string Current = "20261117161849_R3205IndependentPicRoster";
    [Theory][InlineData(false)][InlineData(true)] public void Both_provider_models_are_additive_native_and_forbid_destructive_Down(bool pg) {
        var options = new DbContextOptionsBuilder<IpqcDbContext>(); if (pg) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options); Assert.False(db.Database.HasPendingModelChanges()); var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "CREATE TABLE ").Count); Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql);
        foreach (var text in new[] { "PicRosterEntries", "PicRosterRevisions", "TR_pic_revision", "ON DELETE RESTRICT", "CK_Pic_Revision" }) Assert.Contains(text, sql);
        if (pg) { Assert.Contains("\"PicId\" uuid", sql); Assert.Contains("\"Revision\" bigint", sql); Assert.Contains("timestamp with time zone", sql); Assert.Contains("character varying(200)", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(Current, Previous));
        var entry = db.Model.FindEntityType(typeof(JaxPower.Ipqc.Api.Domain.PicRosterEntry))!; Assert.Empty(entry.GetForeignKeys()); Assert.DoesNotContain(entry.GetProperties(), x => x.Name is "ApplicationUserId" or "LineId");
    }
    [Fact] public async Task Populated_upgrade_preserves_legacy_and_all_accepted_header_sample_photo_facts() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    private static async Task<string> Prior(PhotoFixture p) {
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        var u = await SampleEvidenceTests.Setup(p); await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u));
        _ = await BasicInformationFixture.Accept(u.S, BasicInformationFixture.Input(u.S, 3));
        _ = await p.F.Task(workflow: JaxPower.Ipqc.Api.Domain.WorkflowVersion.LegacyPersonalV1);
        return await BasicInformationFixture.Facts(p.F);
    }
    internal static async Task Upgrade(PhotoFixture p) {
        var before = await Prior(p); await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await BasicInformationFixture.Facts(p.F));
        Assert.Equal(0, await p.F.Read(db => db.PicRosterEntries.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.PicRosterRevisions.CountAsync()));
        var client = p.F.Client((await p.F.Login("admin")).AccessToken); var afterLogin = await BasicInformationFixture.Facts(p.F);
        _ = await Accepted(await client.PostAsJsonAsync(Root, Input())); Assert.Equal(afterLogin, await BasicInformationFixture.Facts(p.F));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => { await db.Database.OpenConnectionAsync(); await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandText = "PRAGMA foreign_key_check";
            await using var r = await q.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true; });
    }
    [Theory][InlineData("CREATE TABLE \"PicRosterRevisions\"")][InlineData("CREATE TRIGGER \"TR_pic_revision\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_upgrade_rolls_back_new_DDL_and_history(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await MigrationRollback(p, point); }
    internal static async Task MigrationRollback(PhotoFixture p, string point) {
        var before = await Prior(p); p.F.Fault.Arm(point, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last())); Assert.Equal(before, await BasicInformationFixture.Facts(p.F));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await BasicInformationFixture.Facts(p.F));
        Assert.Equal(0, await p.F.Read(db => db.PicRosterEntries.CountAsync()));
    }
    [Theory][InlineData("entry", false)][InlineData("revision", false)][InlineData("commit", false)][InlineData("revision", true)][InlineData("commit", true)]
    public async Task Failure_rolls_back_identity_and_the_complete_revision_audit(string point, bool edit) { await using var f = await PersonalSessionFixture.CreateAsync(); await AcceptanceRollback(f, point, edit); }

    internal static async Task AcceptanceRollback(PersonalSessionFixture f, string point, bool edit) {
        var client = f.Client((await f.Login("admin")).AccessToken); PicRevisionReceipt? initial = edit ? (await Accepted(await client.PostAsJsonAsync(Root, Input()))).Receipt : null;
        var input = Input(); var change = new PicEditInput(Guid.NewGuid(), 1, "Failed rename", "Department"); var before = await Facts(f);
        if (point == "commit") f.CommitFault.Armed = true; else f.Fault.Arm(point == "entry" ? "INSERT INTO \"PicRosterEntries\"" : "INSERT INTO \"PicRosterRevisions\"", true);
        async Task<HttpResponseMessage> Send() => initial is null ? await client.PostAsJsonAsync(Root, input) : await client.PutAsJsonAsync(Root + "/" + initial.PicId, change);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Send()).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await Facts(f)); Assert.False((await Accepted(await Send())).ReplayOnly);
        Assert.Equal(edit ? 2 : 1, await f.Read(db => db.PicRosterRevisions.CountAsync())); Assert.Equal(1, await f.Read(db => db.PicRosterEntries.CountAsync()));
    }
    [Fact] public async Task Native_and_tracked_history_guards_reject_mutation_bad_actor_state_and_revision() { await using var f = await PersonalSessionFixture.CreateAsync(); await Integrity(f); }
    internal static async Task Integrity(PersonalSessionFixture f) {
        var client = f.Client((await f.Login("admin")).AccessToken); var first = (await Accepted(await client.PostAsJsonAsync(Root, Input()))).Receipt; var before = await Facts(f);
        foreach (var table in new[] { "PicRosterEntries", "PicRosterRevisions" }) foreach (var action in new[] { "DELETE FROM", "UPDATE" }) {
            var sql = action == "UPDATE" ? $"UPDATE \"{table}\" SET \"PicId\"='00000000-0000-0000-0000-000000000001'" : $"DELETE FROM \"{table}\"";
            var error = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlRawAsync(sql))); Assert.NotNull(error);
            Assert.Contains("immutable", error.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var kind in new[] { "actor", "revision", "active" }) {
            Exception? native = null; var injected = false; f.Fault.ObserveFailure = (_, e) => native = e.GetBaseException();
            f.Fault.ObserveAsync = (command, _) => {
                var m = System.Text.RegularExpressions.Regex.Match(command.CommandText, "INSERT INTO \"PicRosterRevisions\" \\((?<columns>[^)]+)\\)\\s+VALUES \\((?<values>[^)]+)\\)");
                if (!injected && m.Success) {
                    var columns = m.Groups["columns"].Value.Split(',').Select(x => x.Trim().Trim('"')).ToArray(); var parameters = m.Groups["values"].Value.Split(',').Select(x => x.Trim().TrimStart('@')).ToArray();
                    var column = kind == "actor" ? "ActorUserId" : kind == "revision" ? "Revision" : "IsActive";
                    var parameter = command.Parameters.Cast<DbParameter>().Single(p => p.ParameterName.TrimStart('@') == parameters[Array.IndexOf(columns, column)]);
                    parameter.Value = kind == "actor" ? TestAccounts.PqeId : kind == "revision" ? 3L : false; injected = true;
                } return Task.CompletedTask;
            };
            try { Assert.Equal(HttpStatusCode.InternalServerError, (await client.PutAsJsonAsync(Root + "/" + first.PicId, new PicEditInput(Guid.NewGuid(), 1, "Valid new name", "Department"))).StatusCode);
                Assert.True(injected); Assert.NotNull(native); if (f.PostgresRoot is not null) { var pg = Assert.IsType<Npgsql.PostgresException>(native); Assert.Equal("23514", pg.SqlState); Assert.Equal("PicRevisionConflict", pg.MessageText); }
                else { var sq = Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(native); Assert.Equal(1811, sq.SqliteExtendedErrorCode); Assert.Contains("PicRevisionConflict", sq.Message); }
            } finally { f.Fault.ObserveAsync = null; f.Fault.ObserveFailure = null; }
            Assert.Equal(before, await Facts(f));
        }
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.PicRosterRevisions.FirstAsync()).Name = "Rewrite")); Assert.Contains("immutable", tracked.Message);
        Assert.Equal(before, await Facts(f));
    }
}
