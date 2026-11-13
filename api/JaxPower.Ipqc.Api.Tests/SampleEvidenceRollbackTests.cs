using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SampleEvidenceRollbackTests
{
    [Theory][InlineData("UPDATE \"SharedDrafts\"")][InlineData("INSERT INTO \"AcceptedBatchReceipts\"")][InlineData("INSERT INTO \"SampleEvidenceOperations\"")]
    [InlineData("INSERT INTO \"SampleEvidenceProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Every_fresh_failure_is_fully_atomic(string point) { await using var p = await PhotoFixture.Create(); await Failure(p, point, false); }
    [Theory][InlineData("UPDATE \"SampleEvidenceProjections\"")][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task Failed_withdrawal_preserves_active_projection(string point) { await using var p = await PhotoFixture.Create(); await Failure(p, point, true); }
    internal static async Task Failure(PhotoFixture p, string point, bool withdraw) {
        var u = await SampleEvidenceTests.Setup(p); var b = SampleEvidenceTests.Input(u);
        if (withdraw) { var first = (await SampleEvidenceTests.Accept(u, b)).Operation; b = SampleEvidenceTests.Input(u, 3, kind: "WithdrawSampleEvidence", previous: first); }
        var before = await SampleEvidenceTests.Facts(p);
        if (point == "commit") p.F.CommitFault.Armed = true; else p.F.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(SampleEvidenceTests.Url(u.S), b)).StatusCode);
        if (point == "commit") Assert.False(p.F.CommitFault.Armed); else Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p)); Assert.True(File.Exists(await p.FinalPath(u.Photo.AttachmentId)));
        await SampleEvidenceTests.Accept(u, b);
    }
}
