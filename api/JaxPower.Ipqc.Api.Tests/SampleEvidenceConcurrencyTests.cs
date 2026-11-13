using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SampleEvidenceConcurrencyTests
{
    private static async Task Blocked(PersonalSessionFixture f, int holder, Task<int> waiter) {
        if (f.PostgresRoot is not null) await PostgresBlocking.Wait(f.Connection, holder, await waiter.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    [Theory][InlineData(true, "explicit")][InlineData(false, "explicit")][InlineData(true, "reset")][InlineData(false, "reset")]
    [InlineData(true, "role")][InlineData(false, "role")][InlineData(true, "deactivate")][InlineData(false, "deactivate")]
    public async Task Fresh_binding_source_revocation_both_orders(bool first, string action) { await using var p = await PhotoFixture.Create(); await Security(p, first, action); }
    internal static async Task Security(PhotoFixture p, bool revokeFirst, string action) {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F; var b = SampleEvidenceTests.Input(u); var admin = await f.Login("admin");
        async Task Revoke() {
            var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
            var r = action switch {
                "explicit" => await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Synthetic binding race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
                _ => await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null) };
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        var fragment = revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"SampleEvidenceOperations\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); Task<HttpResponseMessage> accepting; Task revoking;
        if (revokeFirst) { revoking = Revoke(); accepting = Task.FromResult(new HttpResponseMessage()); }
        else { accepting = s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), b); revoking = Task.CompletedTask; }
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (revokeFirst) accepting = s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), b); else revoking = Revoke();
            await Blocked(f, holder, waiter.Task);
        } finally { f.Fault.Release.TrySetResult(); }
        await revoking; var result = await accepting; Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        Assert.Equal(revokeFirst ? 2 : 3, await DraftEvidenceAssessmentTests.Version(s));
        Assert.Equal(revokeFirst ? 0 : 1, await f.Read(db => db.SampleEvidenceOperations.CountAsync()));
        if (!revokeFirst) Assert.True((await SampleEvidenceTests.Accept(u, b with { SourceProof = null })).ReplayOnly);
        Assert.Equal(1, await f.Read(db => db.ContributionOperations.CountAsync())); Assert.Equal(1, await f.Read(db => db.DraftAttachmentOperations.CountAsync()));
    }
    [Theory][InlineData("binding")][InlineData("sample")][InlineData("removed")][InlineData("photo")][InlineData("unable")]
    public async Task Every_draft_domain_contends_on_the_same_CAS(string first) { await using var p = await PhotoFixture.Create(); await Cas(p, first); }
    internal static async Task Cas(PhotoFixture p, string first) {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
        Task<HttpResponseMessage> Write(string kind) => kind switch {
            "binding" => s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), SampleEvidenceTests.Input(u)),
            "sample" => s.Client().PostAsJsonAsync(s.Root + "/operation-batches", s.Batch(s.Capture(version: 2), 2)),
            "removed" => s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(s, 2)),
            "photo" => s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, u.Photo, 2, previous: u.Reference, kind: "DetachAttachment")),
            _ => s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), UnableToMeasureTests.Input(new(s, u.Photo, u.Reference), 2)) };
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n) {
                if (c.CommandText.Contains("UPDATE \"SharedDrafts\"", StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm("UPDATE \"SharedDrafts\""); var winning = Write(first); var others = new List<Task<HttpResponseMessage>>();
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            foreach (var k in new[] { "binding", "sample", "removed", "photo", "unable" }.Where(x => x != first)) others.Add(Write(k));
            await Blocked(f, holder, waiter.Task);
        } finally { f.Fault.Release.TrySetResult(); }
        var results = await Task.WhenAll(others.Prepend(winning)); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Equal(4, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(3, await f.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(3, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(3, await f.Read(db => db.AcceptedOperationIdentities.CountAsync()));
    }
    [Theory][InlineData(true, "sample")][InlineData(false, "sample")][InlineData(true, "detach")][InlineData(false, "detach")]
    public async Task Later_sample_or_reference_write_cannot_silently_retarget_binding(bool first, string change) { await using var p = await PhotoFixture.Create(); await LaterWrite(p, first, change); }
    internal static async Task LaterWrite(PhotoFixture p, bool bindingFirst, string change) {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
        var fragment = bindingFirst ? "INSERT INTO \"SampleEvidenceOperations\"" : change == "sample" ? "INSERT INTO \"ContributionOperations\"" : "INSERT INTO \"DraftAttachmentOperations\"";
        // Both commands deliberately request successive versions. The waiter must see committed fresh facts.
        Task<HttpResponseMessage> Binding() => s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), SampleEvidenceTests.Input(u, bindingFirst ? 2 : 3));
        Task<HttpResponseMessage> Change() {
            var version = bindingFirst ? 3 : 2;
            if (change == "detach") return s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, u.Photo, version, previous: u.Reference, kind: "DetachAttachment"));
            var b = s.Mutation(u.Sample, "CorrectSample", new("OK", null, null));
            return s.Client().PostAsJsonAsync(s.Root + "/operation-batches", b with { ExpectedDraftVersion = version, Operations = [b.Operations[0] with { BaseDraftVersion = version }] });
        }
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => { if (c.Connection is NpgsqlConnection n) {
            if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
            else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
        } return Task.CompletedTask; };
        f.Fault.Arm(fragment); var first = bindingFirst ? Binding() : Change(); Task<HttpResponseMessage>? second = null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); second = bindingFirst ? Change() : Binding(); await Blocked(f, holder, waiter.Task); }
        finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode); Assert.Equal(!bindingFirst && change == "detach" ? HttpStatusCode.Conflict : HttpStatusCode.OK, (await second!).StatusCode);
        var read = await SampleEvidenceTests.Read(u);
        if (!bindingFirst && change == "detach") Assert.Empty(read.Bindings);
        else { var b = Assert.Single(read.Bindings); Assert.Equal(u.Sample.OperationId, b.LastOperation.Transition.Operation.OperationId); if (change == "sample") Assert.False(b.IsCurrentSampleValue); else Assert.False(b.IsCurrentPhotoEligible); }
    }
    [Fact] public async Task Lost_response_early_NotFound_and_postcommit_recovery() { await using var p = await PhotoFixture.Create(); await Lost(p); }
    internal static async Task Lost(PhotoFixture p) {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var b = SampleEvidenceTests.Input(u);
        p.F.Fault.Arm("FROM \"RefreshTokenSessions\""); var accepting = s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), b);
        try { await p.F.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(HttpStatusCode.NotFound, (await s.Client(s.A).GetAsync(s.Root + $"/operations/{b.Operation.OperationId}/receipt")).StatusCode); }
        finally { p.F.Fault.Release.TrySetResult(); }
        var response = await accepting; Assert.Equal(HttpStatusCode.OK, response.StatusCode); var original = (await response.Content.ReadFromJsonAsync<SampleEvidenceBatchResult>())!.Operation;
        p.F.Fault.ObserveAsync = (c, _) => { if (c.CommandText.Contains("INSERT INTO \"SampleEvidenceOperations\"", StringComparison.Ordinal)) p.F.CommitFault.AfterCommit = () => throw new InvalidOperationException("Synthetic response lost after binding commit"); return Task.CompletedTask; };
        var withdraw = SampleEvidenceTests.Input(u, 3, kind: "WithdrawSampleEvidence", previous: original);
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), withdraw)).StatusCode); p.F.Fault.ObserveAsync = null;
        Assert.True((await SampleEvidenceTests.Accept(u, withdraw with { SourceProof = null })).ReplayOnly);
        Assert.Equal(4, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(2, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync()));
    }
}
