using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftEvidenceAssessmentConcurrencyTests
{
    private static void Observe(PersonalSessionFixture f, string fragment, Action<int> setHolder, Func<int> holder, TaskCompletionSource<int> waiter) =>
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) setHolder(n.ProcessID);
                else if (holder() != 0 && holder() != n.ProcessID && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
    private static async Task Blocked(PersonalSessionFixture f, int holder, Task<int> waiter) {
        if (f.PostgresRoot is not null) await PostgresBlocking.Wait(f.Connection, holder, await waiter.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Evaluation_and_reference_writer_have_consistent_versions(bool first) { await using var p = await PhotoFixture.Create(); await Reference(p, first); }
    internal static async Task Reference(PhotoFixture p, bool readFirst) {
        var f = p.F; var s = await SharedDraftFixture.Setup(f); await DraftEvidenceAssessmentTests.Sample(s); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var change = DraftAttachmentTests.Input(s, photo, 2, previous: reference, kind: "DetachAttachment");
        var fragment = readFirst ? "FROM \"DraftAttachmentProjections\"" : "UPDATE \"SharedDrafts\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Observe(f, fragment, pid => Interlocked.CompareExchange(ref holder, pid, 0), () => holder, waiter); f.Fault.Arm(fragment);
        Task<HttpResponseMessage>? read = readFirst ? s.Client().GetAsync(DraftEvidenceAssessmentTests.Url(s)) : null;
        Task<HttpResponseMessage>? write = readFirst ? null : s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), change);
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); read ??= s.Client().GetAsync(DraftEvidenceAssessmentTests.Url(s)); write ??= s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), change);
            await Blocked(f, holder, waiter.Task); if (f.PostgresRoot is not null) { Assert.False(read.IsCompleted); Assert.False(write.IsCompleted); } }
        finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.OK, (await write!).StatusCode); Assert.Equal(HttpStatusCode.OK, (await read!).StatusCode);
        var result = (await (await read).Content.ReadFromJsonAsync<DraftEvidenceAssessmentOutput>())!; var item = result.Items.Single(x => x.TaskItemId == s.ItemId);
        Assert.Equal(readFirst ? 2 : 3, result.DraftVersion); Assert.Equal(readFirst ? "Satisfied" : "InvalidEvidence", item.EvidenceStatus);
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(3, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(3, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(DraftEvidenceAssessmentTests.Url(s) + "?expectedDraftVersion=2")).StatusCode);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_family_logout_orders_are_enforced(bool first) { await using var p = await PhotoFixture.Create(); await Logout(p, first); }
    internal static async Task Logout(PhotoFixture p, bool logoutFirst) {
        var f = p.F; var s = await SharedDraftFixture.Setup(f); await DraftEvidenceAssessmentTests.Sample(s); await DraftEvidenceAssessmentTests.Photo(s);
        var fragment = logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "FROM \"DraftAttachmentProjections\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Observe(f, fragment, pid => Interlocked.CompareExchange(ref holder, pid, 0), () => holder, waiter); f.Fault.Arm(fragment);
        Task<HttpResponseMessage>? read = logoutFirst ? null : s.Client().GetAsync(DraftEvidenceAssessmentTests.Url(s));
        Task<HttpResponseMessage>? logout = logoutFirst ? s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)) : null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); read ??= s.Client().GetAsync(DraftEvidenceAssessmentTests.Url(s)); logout ??= s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)); await Blocked(f, holder, waiter.Task); }
        finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NoContent, (await logout!).StatusCode); Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, (await read!).StatusCode);
        Assert.Equal(2, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(2, await f.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync()));
    }
}
