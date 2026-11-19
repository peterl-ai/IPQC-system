using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.OrdinaryNgDetailsFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OrdinaryNgDetailsIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Migration_adds_only_the_ordinary_ledger_and_matches_both_provider_models(bool postgres)
    {
        var options = new DbContextOptionsBuilder<IpqcDbContext>();
        if (postgres) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused");
        else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetService<IMigrator>();
        var sql = migrator.GenerateScript(PriorMigration, CurrentMigration);
        Assert.Single(Regex.Matches(sql, "CREATE TABLE "));
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("\"IX_OrdNg_Scope\"", sql);
        Assert.Contains("\"TR_OrdNg_Insert\"", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains(postgres ? "\"OperationId\" uuid" : "\"OperationId\" TEXT", sql);
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(CurrentMigration, PriorMigration));
    }

    [Fact]
    public async Task Real_upgrade_preserves_every_common_receipt_version_and_legacy_history()
    {
        await using var p = await PhotoFixture.Create(migration: PriorMigration);
        await Upgrade(p);
    }

    internal static async Task Upgrade(PhotoFixture p)
    {
        var fixture = await NgPicSelectionFixture.Setup(p.F);
        var s = fixture.S;
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var bind = new BindingSetup(s, fixture.Sample!, photo, reference!);
        await SampleEvidenceTests.Accept(bind, SampleEvidenceTests.Input(bind, 2));
        await BasicInformationFixture.Accept(s, BasicInformationFixture.Input(s, 3));
        await NgPicSelectionFixture.Accept(fixture, NgPicSelectionFixture.Input(fixture, 4));
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 5, other));
        var unable = new UnableSetup(s, photo, reference!);
        await UnableToMeasureTests.Accept(unable, UnableToMeasureTests.Input(unable, 6));
        var legacy = await p.F.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        await p.F.Edit(async db => (await db.PatrolTaskItems.FirstAsync(x => x.PatrolTaskId == legacy)).Remarks = "Synthetic legacy value");

        var old = await p.F.Read(db => db.AcceptedBatchReceipts.AsNoTracking().OrderBy(r => r.DraftVersion).ToListAsync());
        var responses = new Dictionary<Guid, string>();
        foreach (var previous in old)
            responses.Add(previous.BatchId, await s.Client().GetStringAsync(CommonUrl(s, previous.BatchId)));
        var versions = responses.Values.Select(x => JsonNode.Parse(x)!["receiptVersion"]?.GetValue<int>()).Order().ToArray();
        Assert.Equal(new int?[] { null, 2, 3, 4, 5, 6, 7 }, versions);

        var before = await NgPicSelectionFixture.Facts(p.F);
        Assert.Equal(PriorMigration, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        await p.F.Edit(async db => await db.Database.MigrateAsync());
        Assert.Equal(CurrentMigration, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        Assert.Equal(before, await NgPicSelectionFixture.Facts(p.F));
        Assert.Empty(await p.F.Read(db => db.OrdinaryNgDetailsOperations.ToListAsync()));
        foreach (var (batch, bytes) in responses)
        {
            Assert.DoesNotContain("ordinaryNgDetailsOperations", bytes);
            Assert.Equal(bytes, await s.Client().GetStringAsync(CommonUrl(s, batch)));
        }
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("audit")]
    [InlineData("commit")]
    public async Task Failed_acceptance_rolls_back_CAS_receipt_registry_and_ledger(string stage)
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Rollback(f, stage);
    }

    internal static async Task Rollback(PersonalSessionFixture f, string stage)
    {
        var s = await SharedDraftFixture.Setup(f);
        await DraftEvidenceAssessmentTests.Sample(s);
        var body = Input(s);
        var before = await Facts(f);
        if (stage == "commit") f.CommitFault.Armed = true;
        else f.Fault.Arm(stage == "operation" ? "INSERT INTO \"OrdinaryNgDetailsOperations\"" : "INSERT INTO \"SharedAcceptanceAudits\"", true);
        using var failure = await s.Client().PostAsJsonAsync(WriteUrl(s), body);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        if (stage == "commit") Assert.False(f.CommitFault.Armed);
        else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await Facts(f));
        Assert.False((await Accept(s, body)).ReplayOnly);
    }

    [Fact]
    public async Task Immutable_rows_and_cross_domain_identity_are_guarded_by_the_actual_provider()
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Immutable(f);
    }

    internal static async Task Immutable(PersonalSessionFixture f)
    {
        var s = await SharedDraftFixture.Setup(f);
        var sample = await DraftEvidenceAssessmentTests.Sample(s);
        var first = (await Accept(s, Input(s))).Operation;
        var before = await Facts(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db =>
            (await db.OrdinaryNgDetailsOperations.SingleAsync()).Remarks = "Synthetic rewrite"));

        await AssertNative(() => f.Edit(db => db.OrdinaryNgDetailsOperations.ExecuteUpdateAsync(
            x => x.SetProperty(o => o.Remarks, "Synthetic rewrite"))), "immutable", f);
        await AssertNative(() => f.Edit(db => db.OrdinaryNgDetailsOperations.ExecuteDeleteAsync()), "immutable", f);
        if (f.PostgresRoot is not null)
            await AssertNative(() => f.Edit(db => db.Database.ExecuteSqlRawAsync("""TRUNCATE "OrdinaryNgDetailsOperations" """)), "immutable", f);

        // Inject a duplicate into the native INSERT after application admission accepted a genuine fresh id.
        const string table = "OrdinaryNgDetailsOperations";
        Exception? providerFailure = null;
        f.Fault.ObserveFailure = (_, error) => providerFailure = error.GetBaseException();
        f.Fault.ObserveAsync = (command, _) =>
        {
            var parameter = Parameter(command, table, "OperationId");
            if (parameter is not null) parameter.Value = sample.OperationId;
            return Task.CompletedTask;
        };
        var correction = Input(s, 2, first);
        try
        {
            using var duplicate = await s.Client().PostAsJsonAsync(WriteUrl(s), correction);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains("OperationIdentityConflict", await duplicate.Content.ReadAsStringAsync());
            Assert.Contains("OperationIdentityConflict", providerFailure?.Message);
        }
        finally
        {
            f.Fault.ObserveAsync = null;
            f.Fault.ObserveFailure = null;
        }
        Assert.Equal(before, await Facts(f));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("basis")]
    public async Task Native_insert_rejects_staged_metadata_with_false_scope_or_no_real_NG_basis(string scenario)
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Tamper(f, scenario);
    }

    internal static async Task Tamper(PersonalSessionFixture f, string scenario)
    {
        var s = await SharedDraftFixture.Setup(f);
        await DraftEvidenceAssessmentTests.Sample(s);
        var before = await Facts(f);
        Exception? native = null;
        f.Fault.ObserveFailure = (_, ex) => native = ex.GetBaseException();
        f.Fault.ObserveAsync = (command, _) =>
        {
            if (scenario == "actor" && Parameter(command, "OrdinaryNgDetailsOperations", "ActorUserId") is { } actor)
                actor.Value = s.A.User.Id;
            if (scenario == "basis")
            {
                if (Parameter(command, "OrdinaryNgDetailsOperations", "NgBasisJson") is { } basis) basis.Value = "[]";
                foreach (var table in new[] { "AcceptedBatchReceipts", "OrdinaryNgDetailsOperations" })
                {
                    if (Parameter(command, table, "ReceiptJson") is not { Value: string value } receipt) continue;
                    var node = JsonNode.Parse(value)!;
                    if (table == "AcceptedBatchReceipts") node["ordinaryNgDetailsOperations"]![0]!["acceptedNgBasis"] = new JsonArray();
                    else node["acceptedNgBasis"] = new JsonArray();
                    receipt.Value = node.ToJsonString();
                }
            }
            return Task.CompletedTask;
        };
        try
        {
            using var result = await s.Client().PostAsJsonAsync(WriteUrl(s), Input(s));
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            CheckNative(native, "OrdinaryNgDetailsScopeConflict", f);
        }
        finally
        {
            f.Fault.ObserveAsync = null;
            f.Fault.ObserveFailure = null;
        }
        Assert.Equal(before, await Facts(f));
    }

    private static DbParameter? Parameter(DbCommand command, string table, string column)
    {
        var sql = Regex.Match(command.CommandText, "INSERT INTO \"" + table + "\" \\((?<columns>[^)]+)\\)\\s+VALUES \\((?<values>[^)]+)\\)");
        if (!sql.Success) return null;
        var columns = sql.Groups["columns"].Value.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
        var parameters = sql.Groups["values"].Value.Split(',').Select(x => x.Trim().TrimStart('@')).ToArray();
        return command.Parameters.Cast<DbParameter>().Single(p => p.ParameterName.TrimStart('@') == parameters[Array.IndexOf(columns, column)]);
    }

    private static async Task AssertNative(Func<Task> write, string message, PersonalSessionFixture f) =>
        CheckNative((await Record.ExceptionAsync(write))?.GetBaseException(), message, f);

    private static void CheckNative(Exception? error, string message, PersonalSessionFixture f)
    {
        if (f.PostgresRoot is not null) Assert.Equal("23514", Assert.IsType<PostgresException>(error).SqlState);
        else Assert.Equal(1811, Assert.IsType<SqliteException>(error).SqliteExtendedErrorCode);
        Assert.Contains(message, error!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
