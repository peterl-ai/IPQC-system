using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasureRollbackTests
{
    [Theory][InlineData("UPDATE \"SharedDrafts\"")][InlineData("INSERT INTO \"AcceptedBatchReceipts\"")][InlineData("INSERT INTO \"UnableToMeasureOperations\"")]
    [InlineData("INSERT INTO \"UnableToMeasureProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Fresh_failure_rolls_back_every_fact(string point) { await using var p = await PhotoFixture.Create(); await Fresh(p, point); }
    internal static async Task Fresh(PhotoFixture p, string point) {
        var u = await UnableToMeasureTests.Setup(p); var f = p.F; var b = UnableToMeasureTests.Input(u);
        var before = await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new { Receipts = await db.AcceptedBatchReceipts.ToListAsync(), Keys = await db.AcceptedOperationIdentities.ToListAsync() }));
        if (point == "commit") f.CommitFault.Armed = true; else f.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(UnableToMeasureTests.Url(u.S), b)).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        await UnableToMeasureTests.Counts(u, 1, 0, false); Assert.Equal(0, await f.Read(db => db.UnableToMeasureProjections.CountAsync()));
        Assert.Equal(before, await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new { Receipts = await db.AcceptedBatchReceipts.ToListAsync(), Keys = await db.AcceptedOperationIdentities.ToListAsync() })));
        Assert.Equal(1, await f.Read(db => db.DraftAttachmentOperations.CountAsync())); Assert.True(File.Exists(await p.FinalPath(u.Photo.AttachmentId)));
        await UnableToMeasureTests.Accept(u, b); await UnableToMeasureTests.Counts(u, 2, 1, true);
    }
    [Theory][InlineData("UPDATE \"UnableToMeasureProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Failed_clear_retains_previous_active_reason_and_evidence(string point) { await using var p = await PhotoFixture.Create(); await Clear(p, point); }
    internal static async Task Clear(PhotoFixture p, string point) {
        var u = await UnableToMeasureTests.Setup(p); var first = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation;
        var b = UnableToMeasureTests.Input(u, 2, kind: "ClearUnableToMeasureNG", previous: first); var old = await p.F.Read(db => db.UnableToMeasureOperations.Select(x => x.ReceiptJson).SingleAsync());
        if (point == "commit") p.F.CommitFault.Armed = true; else p.F.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(UnableToMeasureTests.Url(u.S), b)).StatusCode);
        if (point == "commit") Assert.False(p.F.CommitFault.Armed); else Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        await UnableToMeasureTests.Counts(u, 2, 1, true); Assert.Equal(old, await p.F.Read(db => db.UnableToMeasureOperations.Select(x => x.ReceiptJson).SingleAsync()));
        Assert.Equal("Satisfied", (await UnableToMeasureTests.Read(u)).Items.Single(x => x.TaskItemId == u.S.ItemId).DraftEvidenceStatus);
        await UnableToMeasureTests.Accept(u, b); await UnableToMeasureTests.Counts(u, 3, 2, false);
    }
}
