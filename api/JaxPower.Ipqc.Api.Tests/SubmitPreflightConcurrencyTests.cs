using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SubmitPreflightConcurrencyTests
{
    private static async Task Race(PhotoFixture p, string fragment, Func<Task> first, Func<Task> second) {
        var f = p.F; var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && holder != n.ProcessID && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); var one = first(); Task? two = null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); two = second();
            if (f.PostgresRoot is not null) {
                await PostgresBlocking.Wait(f.Connection, holder, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.False(one.IsCompleted); Assert.False(two.IsCompleted);
            }
        } finally { f.Fault.Release.TrySetResult(); }
        await one; await two!;
    }
    [Theory][InlineData(true, "sample")][InlineData(false, "sample")][InlineData(true, "detach")][InlineData(false, "detach")]
    [InlineData(true, "removed")][InlineData(false, "removed")][InlineData(true, "unable")][InlineData(false, "unable")]
    public async Task Cross_account_draft_writes_and_preflight_have_one_consistent_version(bool first, string kind) { await using var p = await PhotoFixture.Create(); await Writer(p, first, kind); }
    internal static async Task Writer(PhotoFixture p, bool readFirst, string kind) {
        var f = p.F; var s = await SubmitPreflightTests.Setup(p, minimum: "2"); await DraftEvidenceAssessmentTests.Sample(s);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); SubmitPreflightOutput? observed = null;
        // Different current users: prove Task serialization, rather than only the requester-account lock.
        async Task Read() { observed = await SubmitPreflightTests.Read(s); }
        async Task Write() {
            var client = s.Client(s.A); HttpResponseMessage result = kind switch {
                "sample" => await client.PostAsJsonAsync(s.Root + "/operation-batches", s.Batch(s.Capture(new("OK", null, null), version: 2), 2)),
                "detach" => await client.PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, photo, 2, previous: reference, kind: "DetachAttachment")),
                "removed" => await client.PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(s, 2)),
                _ => await client.PostAsJsonAsync(UnableToMeasureTests.Url(s), UnableToMeasureTests.Input(new(s, photo, reference!), 2)) };
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        }
        await Race(p, readFirst ? "FROM \"ContributionOperations\"" : "UPDATE \"SharedDrafts\"", readFirst ? Read : Write, readFirst ? Write : Read);
        Assert.NotNull(observed); Assert.Equal(readFirst ? 2 : 3, observed.DraftVersion);
        var i = SubmitPreflightTests.Item(observed, s).Evidence;
        Assert.Equal(!readFirst && kind == "removed" ? "Removed" : !readFirst && kind == "unable" ? "UnableToMeasureNG" : "Applicable", i.Disposition);
        Assert.Equal(!readFirst && kind == "sample" ? 2 : 1, i.ActiveSampleCount); Assert.Equal(2, i.MinimumSampleCount);
        Assert.Equal(!readFirst && kind == "detach" ? "InvalidEvidence" : "Satisfied", i.EvidenceStatus);
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(3, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(3, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(SubmitPreflightTests.Url(s) + "?expectedDraftVersion=2")).StatusCode);
        await SubmitPreflightTests.NoWrites(p, s);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Frozen_policy_read_and_preflight_preserve_the_same_frozen_facts(bool first) { await using var p = await PhotoFixture.Create(); await PolicyRead(p, first); }
    internal static async Task PolicyRead(PhotoFixture p, bool preflightFirst) {
        var s = await SubmitPreflightTests.Setup(p, "Required"); await DraftEvidenceAssessmentTests.Sample(s, "OK"); await DraftEvidenceAssessmentTests.Photo(s);
        var before = await SampleEvidenceTests.Facts(p); SubmitPreflightOutput? preflight = null; TaskPhotoPolicies? policies = null;
        async Task Read() { preflight = await SubmitPreflightTests.Read(s); }
        async Task Policy() { policies = await s.Client(s.A).GetFromJsonAsync<TaskPhotoPolicies>($"/api/line-patrol/tasks/{s.TaskId}/photo-policies"); }
        await Race(p, "FROM \"FrozenTaskPhotoPolicies\"", preflightFirst ? Read : Policy, preflightFirst ? Policy : Read);
        Assert.Equal(2, preflight!.DraftVersion); Assert.NotNull(policies); Assert.Equal(2, policies.Items.Count);
        Assert.All(preflight.Items, i => { Assert.True(i.Evidence.PhotoPolicy.HasExplicitConfirmation); Assert.Equal("Required", i.Evidence.PhotoPolicy.RecognizedText); });
        Assert.Equal(before, await SampleEvidenceTests.Facts(p)); await SubmitPreflightTests.NoWrites(p, s);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_family_logout_is_ordered_with_preflight(bool first) { await using var p = await PhotoFixture.Create(); await Logout(p, first); }
    internal static async Task Logout(PhotoFixture p, bool logoutFirst) {
        var s = await SubmitPreflightTests.Setup(p); await DraftEvidenceAssessmentTests.Sample(s); await DraftEvidenceAssessmentTests.Photo(s);
        string? afterLogout = null; HttpResponseMessage? result = null;
        async Task Read() { result = await s.Client().GetAsync(SubmitPreflightTests.Url(s)); }
        async Task Logout() {
            Assert.Equal(HttpStatusCode.NoContent, (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).StatusCode);
            afterLogout = await SampleEvidenceTests.Facts(p);
        }
        await Race(p, logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "FROM \"ContributionOperations\"", logoutFirst ? Logout : Read, logoutFirst ? Read : Logout);
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, result!.StatusCode);
        Assert.Equal(afterLogout, await SampleEvidenceTests.Facts(p)); Assert.Equal(2, await DraftEvidenceAssessmentTests.Version(s));
        Assert.Equal(2, await p.F.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(2, await p.F.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(0, await p.F.Read(db => db.PatrolTaskSubmissions.CountAsync()));
    }
}
