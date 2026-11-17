using System.Data.Common;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.BasicInformationFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationMigrationTests
{
    [Theory][InlineData(false)][InlineData(true)] public void Both_models_have_two_additive_tables_native_types_and_no_destructive_Down(bool postgres) {
        var options = new DbContextOptionsBuilder<IpqcDbContext>(); if (postgres) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options); Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "CREATE TABLE ").Count);
        foreach (var table in new[] { "TaskHeaderOperations", "TaskHeaderProjections" }) Assert.Contains("CREATE TABLE \"" + table + "\"", sql);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        foreach (var name in new[] { "TR_header_identity", "TR_header_scope", "TR_header_projection_update", "BasicInformationField", "FK_Header_Predecessor" }) Assert.Contains(name, sql);
        if (postgres) { Assert.Contains("\"OperationId\" uuid", sql); Assert.Contains("\"DraftVersion\" bigint", sql); Assert.Contains("timestamp with time zone", sql); Assert.Contains("character varying(200)", sql); }
        foreach (var entity in db.Model.GetEntityTypes().Where(x => x.GetTableName() is "TaskHeaderOperations" or "TaskHeaderProjections")) {
            foreach (var name in entity.GetForeignKeys().Select(x => x.GetConstraintName()).Concat(entity.GetIndexes().Select(x => x.GetDatabaseName()))) Assert.True(name!.Length <= 63, name);
        }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(Current, Previous));
    }
    [Fact] public async Task Populated_upgrade_preserves_all_existing_rows_columns_indexes_FKs_and_seeds_no_values() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    internal static async Task<BindingSetup> Prior(PhotoFixture p) {
        // Both providers must really start before the additive migration, never test an already-current schema.
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        var u = await SampleEvidenceTests.Setup(p); await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u));
        return u;
    }
    internal static async Task Upgrade(PhotoFixture p) {
        var u = await Prior(p); var before = await SampleEvidenceTests.Facts(p);
        var oldReceipts = await p.F.Read(db => db.AcceptedBatchReceipts.OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync());
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        Assert.Equal(0, await p.F.Read(db => db.TaskHeaderOperations.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.TaskHeaderProjections.CountAsync()));
        var empty = await Read(u.S); Assert.Equal(3, empty.DraftVersion); Assert.Equal(0, empty.AcceptedFieldCount); Assert.Null(empty.Values.Customer);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        _ = await Accept(u.S, Input(u.S, 3));
        Assert.Equal(oldReceipts, await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.DraftVersion <= 3).OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync()));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => {
            await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await command.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync()); return true;
        });
    }
    [Theory][InlineData("CREATE TABLE \"TaskHeaderProjections\"")][InlineData("CREATE TRIGGER \"TR_header_scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_additive_upgrade_rolls_back_DDL_guards_and_migration_history(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await MigrationRollback(p, point); }
    internal static async Task MigrationRollback(PhotoFixture p, string point) {
        _ = await Prior(p); var before = await SampleEvidenceTests.Facts(p); p.F.Fault.Arm(point, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last())); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await SampleEvidenceTests.Facts(p)); Assert.Equal(0, await p.F.Read(db => db.TaskHeaderOperations.CountAsync()));
    }
    [Theory][InlineData("UPDATE \"SharedDrafts\"")][InlineData("INSERT INTO \"AcceptedBatchReceipts\"")][InlineData("INSERT INTO \"TaskHeaderOperations\"")]
    [InlineData("INSERT INTO \"TaskHeaderProjections\"")][InlineData("UPDATE \"TaskHeaderProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Every_injected_failure_rolls_back_header_version_receipt_projection_registry_and_audit(string point) { await using var f = await PersonalSessionFixture.CreateAsync(); await AcceptanceRollback(f, point); }
    internal static async Task AcceptanceRollback(PersonalSessionFixture f, string point) {
        var s = await SharedDraftFixture.Setup(f); BasicInformationReceipt? first = null;
        if (point == "UPDATE \"TaskHeaderProjections\"") first = (await Accept(s, Input(s))).Operation;
        var version = first is null ? 0 : 1; var before = await Facts(f); var b = Input(s, version, value: "Must roll back", previous: first);
        if (point == "commit") f.CommitFault.Armed = true; else f.Fault.Arm(point, true);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(WriteUrl(s), b)).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await Facts(f)); await Counts(s, version, version, version);
        Assert.False((await Accept(s, b)).ReplayOnly); await Counts(s, version + 1, version + 1, 1);
    }
    [Fact] public async Task Native_and_EF_guards_prevent_history_mutation_and_projection_rewind_or_scope_change() { await using var f = await PersonalSessionFixture.CreateAsync(); await Integrity(f); }
    internal static async Task Integrity(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var first = (await Accept(s, Input(s))).Operation;
        var latest = (await Accept(s, Input(s, 1, value: "Current accepted correction", previous: first))).Operation;
        var before = await Facts(f);
        async Task Reject(Func<Task> action) {
            var failure = await Record.ExceptionAsync(action); Assert.NotNull(failure);
            if (f.PostgresRoot is not null) Assert.Contains(Assert.IsType<Npgsql.PostgresException>(failure.GetBaseException()).SqlState, new[] { "23514", "23503", "23505" });
            else Assert.Contains(Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(failure.GetBaseException()).SqliteExtendedErrorCode, new[] { 1811, 787, 1555, 2067, 275 });
        }
        foreach (var sql in new[] { "UPDATE \"TaskHeaderOperations\" SET \"Value\"='rewrite'", "DELETE FROM \"TaskHeaderOperations\"", "DELETE FROM \"TaskHeaderProjections\"", "UPDATE \"TaskHeaderProjections\" SET \"Field\"='Production'" })
            await Reject(() => f.Edit(db => db.Database.ExecuteSqlRawAsync(sql)));
        await Reject(() => f.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"TaskHeaderProjections\" SET \"LastOperationId\"={first.OperationId}")));
        foreach (var kind in new[] { "actor", "source", "value", "version", "field", "old-batch" }) {
            // Mutate exactly one SQL parameter of a genuine valid acceptance, after its real batch/version claim.
            // Assert the intended native error, so unrelated ID/receipt/unique failures cannot make this test pass.
            Exception? nativeFailure = null; var injected = false;
            f.Fault.ObserveFailure = (_, ex) => nativeFailure = ex.GetBaseException();
            f.Fault.ObserveAsync = (command, _) => {
                var insert = System.Text.RegularExpressions.Regex.Match(command.CommandText, "INSERT INTO \"TaskHeaderOperations\" \\((?<columns>[^)]+)\\)\\s+VALUES \\((?<values>[^)]+)\\)");
                if (!injected && insert.Success) {
                    var columns = insert.Groups["columns"].Value.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
                    var parameters = insert.Groups["values"].Value.Split(',').Select(x => x.Trim()).ToArray();
                    var column = kind switch { "actor" => "ActorUserId", "source" => "ContributorUserId", "value" => "Value", "version" => "DraftVersion", "field" => "Field", _ => "BatchId" };
                    var name = parameters[Array.IndexOf(columns, column)].TrimStart('@');
                    var parameter = command.Parameters.Cast<DbParameter>().Single(p => p.ParameterName.TrimStart('@') == name);
                    parameter.Value = kind switch { "actor" => TestAccounts.AdminId, "source" => s.B.User.Id, "value" => "Receipt/ledger inconsistency", "version" => 4L, "field" => "ProductMo", _ => first.BatchId };
                    injected = true;
                } return Task.CompletedTask;
            };
            try {
                Assert.Equal(System.Net.HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(WriteUrl(s), Input(s, 2, BasicInformationField.Production))).StatusCode);
                Assert.True(injected); Assert.NotNull(nativeFailure);
                if (f.PostgresRoot is not null) { var pg = Assert.IsType<Npgsql.PostgresException>(nativeFailure); Assert.Equal("23514", pg.SqlState); Assert.Equal("BasicInformationScopeConflict", pg.MessageText); }
                else { var sqlite = Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(nativeFailure); Assert.Equal(1811, sqlite.SqliteExtendedErrorCode); Assert.Contains("BasicInformationScopeConflict", sqlite.Message); }
            } finally { f.Fault.ObserveAsync = null; f.Fault.ObserveFailure = null; }
            Assert.Equal(before, await Facts(f));
        }
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.TaskHeaderOperations.FirstAsync()).ReceiptJson = "{}")); Assert.Contains("immutable", tracked.Message);
        Assert.Equal(before, await Facts(f)); Assert.Equal(latest, (await Read(s)).Fields[0].LastOperation);
    }
}
