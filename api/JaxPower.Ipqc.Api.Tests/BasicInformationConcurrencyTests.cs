using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.BasicInformationFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationConcurrencyTests
{
    private static async Task Race(PersonalSessionFixture f, string fragment, Func<Task> first, Func<Task> second) {
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection pg && command.Transaction is not null) {
                if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, pg.ProcessID, 0);
                else if (holder != 0 && holder != pg.ProcessID && command.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(pg.ProcessID);
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
        await one; await two!; f.Fault.ObserveAsync = null;
    }
    [Theory][InlineData(true, "sample")][InlineData(false, "sample")][InlineData(true, "removed")][InlineData(false, "removed")]
    [InlineData(true, "unable")][InlineData(false, "unable")][InlineData(true, "attachment")][InlineData(false, "attachment")]
    [InlineData(true, "binding")][InlineData(false, "binding")][InlineData(true, "header-same")][InlineData(false, "header-same")]
    [InlineData(true, "header-other")][InlineData(false, "header-other")]
    public async Task All_domains_including_independent_header_fields_contend_on_one_Task_CAS(bool first, string kind) { await using var p = await PhotoFixture.Create(); await Cas(p, first, kind); }
    internal static async Task Cas(PhotoFixture p, bool headerFirst, string kind) {
        var f = p.F; var s = await SharedDraftFixture.Setup(f); var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B);
        var sample = await DraftEvidenceAssessmentTests.Sample(s);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s with { Source = sourceB });
        var u = new BindingSetup(s, sample, photo, reference!);
        var otherS = s with { Source = sourceB }; var header = Input(s, 2); var otherHeader = Input(s, 2,
            kind == "header-other" ? BasicInformationField.Production : BasicInformationField.Customer, "Other user's field", source: sourceB);
        HttpResponseMessage? one = null, two = null;
        async Task Header() { one = await s.Client(s.A).PostAsJsonAsync(WriteUrl(s), header); }
        async Task Other() { var client = s.Client(s.B); two = kind switch {
            "sample" => await client.PostAsJsonAsync(s.Root + "/operation-batches", otherS.Batch(otherS.Capture(new("OK", null, null), version: 2), 2)),
            "removed" => await client.PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(otherS, 2)),
            "unable" => await client.PostAsJsonAsync(UnableToMeasureTests.Url(s), UnableToMeasureTests.Input(new(s, u.Photo, u.Reference), 2, sourceB)),
            "attachment" => await client.PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, u.Photo, 2, sourceB, u.Reference, "DetachAttachment")),
            "binding" => await client.PostAsJsonAsync(SampleEvidenceTests.Url(s), SampleEvidenceTests.Input(u, 2, sourceB)),
            _ => await client.PostAsJsonAsync(WriteUrl(s), otherHeader) };
        }
        // Requester/source accounts differ. Real PG wait therefore reaches the shared Task, not only one account lock.
        await Race(f, "UPDATE \"SharedDrafts\"", headerFirst ? Header : Other, headerFirst ? Other : Header);
        Assert.Equal(headerFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, one!.StatusCode);
        Assert.Equal(headerFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, two!.StatusCode);
        var loser = headerFirst ? two : one; Assert.Contains("DraftVersionConflict", await loser.Content.ReadAsStringAsync());
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(3, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(3, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        var headerWon = headerFirst || kind.StartsWith("header-", StringComparison.Ordinal);
        Assert.Equal(headerWon ? 1 : 0, await f.Read(db => db.TaskHeaderOperations.CountAsync()));
        var r = await Read(s); Assert.Equal(3, r.DraftVersion); Assert.Equal(headerWon ? 1 : 0, r.AcceptedFieldCount);
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskReviews.CountAsync()));
        if (kind == "header-other") {
            // Explicit refresh/reapply uses a new ID; independent fields are never silently merged on stale CAS.
            var old = headerFirst ? otherHeader : header; var rebased = Input(s, 3, old.Operation.Field, old.Operation.Value, source: headerFirst ? sourceB : s.Source);
            Assert.NotEqual(old.Operation.OperationId, rebased.Operation.OperationId);
            _ = await Accept(s, rebased, headerFirst ? s.B : s.A); Assert.Equal(2, (await Read(s)).AcceptedFieldCount);
        }
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Header_read_and_correction_never_mix_versions(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await ReadRace(f, first); }
    internal static async Task ReadRace(PersonalSessionFixture f, bool readFirst) {
        var s = await SharedDraftFixture.Setup(f); var first = (await Accept(s, Input(s))).Operation; var change = Input(s, 1, value: "Concurrent correction", previous: first);
        BasicInformationOutput? read = null;
        async Task ReadCurrent() { read = await Read(s); }
        async Task Write() { _ = await Accept(s, change, s.A); }
        await Race(f, readFirst ? "FROM \"TaskHeaderProjections\"" : "UPDATE \"SharedDrafts\"", readFirst ? ReadCurrent : Write, readFirst ? Write : ReadCurrent);
        Assert.Equal(readFirst ? 1 : 2, read!.DraftVersion); Assert.Equal(readFirst ? "Synthetic customer" : "Concurrent correction", read.Values.Customer);
        Assert.Equal(readFirst ? 1 : 2, read.History.Count); Assert.Equal(2, (await Read(s)).DraftVersion); await Counts(s, 2, 2, 1);
    }
    [Theory][InlineData(true, "reset")][InlineData(false, "reset")][InlineData(true, "role")][InlineData(false, "role")]
    [InlineData(true, "deactivate")][InlineData(false, "deactivate")][InlineData(true, "explicit")][InlineData(false, "explicit")]
    public async Task Source_security_changes_and_acceptance_have_a_real_frontier(bool first, string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await SecurityRace(f, first, action); }
    internal static async Task SecurityRace(PersonalSessionFixture f, bool revokeFirst, string action) {
        var s = await SharedDraftFixture.Setup(f); var input = Input(s); var admin = await f.Login("admin"); HttpResponseMessage? accepted = null;
        var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()));
        async Task AcceptHeader() { accepted = await s.Client().PostAsJsonAsync(WriteUrl(s), input); }
        async Task Revoke() {
            var result = action switch {
                "explicit" => await s.Client(admin).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Synthetic header source race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{s.A.User.Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{s.A.User.Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
                _ => await legacy.PostAsync($"/api/users/{s.A.User.Id}/deactivate", null) };
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        }
        var fragment = revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"TaskHeaderOperations\"";
        await Race(f, fragment, revokeFirst ? Revoke : AcceptHeader, revokeFirst ? AcceptHeader : Revoke);
        Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, accepted!.StatusCode);
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        if (!revokeFirst) Assert.True((await Accept(s, input with { SourceProof = null })).ReplayOnly);
        await Counts(s, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_uploader_logout_does_not_become_source_revocation(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await LogoutRace(f, first); }
    internal static async Task LogoutRace(PersonalSessionFixture f, bool logoutFirst) {
        var s = await SharedDraftFixture.Setup(f); var input = Input(s); HttpResponseMessage? result = null;
        async Task Header() { result = await s.Client().PostAsJsonAsync(WriteUrl(s), input); }
        async Task Logout() { Assert.Equal(HttpStatusCode.NoContent, (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).StatusCode); }
        await Race(f, logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"TaskHeaderOperations\"", logoutFirst ? Logout : Header, logoutFirst ? Header : Logout);
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, result!.StatusCode);
        Assert.Null(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        var before = await Facts(f); var currentA = s with { B = s.A }; _ = await Read(currentA); Assert.Equal(before, await Facts(f));
        await Counts(s, logoutFirst ? 0 : 1, logoutFirst ? 0 : 1, logoutFirst ? 0 : 1);
    }
}
