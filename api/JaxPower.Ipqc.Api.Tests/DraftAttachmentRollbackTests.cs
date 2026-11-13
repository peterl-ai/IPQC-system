using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftAttachmentRollbackTests
{
    [Theory][InlineData("INSERT INTO \"SharedDrafts\"")][InlineData("UPDATE \"SharedDrafts\"")][InlineData("INSERT INTO \"AcceptedBatchReceipts\"")]
    [InlineData("INSERT INTO \"DraftAttachmentOperations\"")][InlineData("INSERT INTO \"DraftAttachmentProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Every_new_acceptance_failure_rolls_back(string point) { await using var p = await PhotoFixture.Create(); await Fresh(p, point); }
    internal static async Task Fresh(PhotoFixture p, string point) {
        var (s, a, bytes) = await DraftAttachmentTests.Setup(p); var b = DraftAttachmentTests.Input(s, a);
        if (point == "commit") p.F.CommitFault.Armed = true; else p.F.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b)).StatusCode);
        if (point == "commit") Assert.False(p.F.CommitFault.Armed); else Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        await DraftAttachmentTests.Counts(s, 0, 0, 0, 0); Assert.Equal(0, await p.F.Read(db => db.SharedDrafts.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.DraftOperationKeys.CountAsync()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(await p.FinalPath(a.AttachmentId))); Assert.Equal(1, await p.F.Read(db => db.AttachmentAcceptances.CountAsync()));
        await DraftAttachmentTests.Accept(s, b); await DraftAttachmentTests.Counts(s, 1, 1, 1, 1);
    }
    [Theory][InlineData("UPDATE \"DraftAttachmentProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Detach_failure_preserves_association(string point) { await using var p = await PhotoFixture.Create(); await Detach(p, point); }
    internal static async Task Detach(PhotoFixture p, string point) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var first = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a))).Operations);
        var before = await p.F.Read(db => db.AcceptedBatchReceipts.Select(x => x.ReceiptJson).SingleAsync()); var b = DraftAttachmentTests.Input(s, a, 1, previous: first, kind: "DetachAttachment");
        if (point == "commit") p.F.CommitFault.Armed = true; else p.F.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b)).StatusCode);
        if (point == "commit") Assert.False(p.F.CommitFault.Armed); else Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        await DraftAttachmentTests.Counts(s, 1, 1, 1, 1); Assert.Equal(before, await p.F.Read(db => db.AcceptedBatchReceipts.Select(x => x.ReceiptJson).SingleAsync()));
        await DraftAttachmentTests.Accept(s, b); await DraftAttachmentTests.Counts(s, 2, 2, 0, 2);
    }
}
