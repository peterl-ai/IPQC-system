using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.NgPicSelectionFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class NgPicSelectionConcurrencyTests
{
    internal static async Task Race(PersonalSessionFixture f, string fragment, Func<Task> first, Func<Task> second, string frontier) {
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection pg && command.Transaction is not null) {
                if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, pg.ProcessID, 0);
                else if (holder != 0 && holder != pg.ProcessID && command.CommandText.Contains(frontier, StringComparison.Ordinal) && command.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(pg.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); var one = first(); Task? two = null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); two = second();
            if (f.PostgresRoot is not null) { await PostgresBlocking.Wait(f.Connection, holder, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10))); Assert.False(one.IsCompleted); Assert.False(two.IsCompleted); }
        } finally { f.Fault.Release.TrySetResult(); }
        await one; await two!; f.Fault.ObserveAsync = null;
    }
    [Theory][InlineData(true, false)][InlineData(false, false)][InlineData(true, true)][InlineData(false, true)]
    public async Task Actual_Admin_and_selector_serialize_both_roster_orders(bool selectionFirst, bool rename) { await using var f = await PersonalSessionFixture.CreateAsync(); await Roster(f, selectionFirst, rename); }
    internal static async Task Roster(PersonalSessionFixture f, bool selectionFirst, bool rename) {
        var u = await Setup(f); var s = u.S; var input = Input(u); HttpResponseMessage? selection = null;
        async Task Select() { selection = await s.Client().PostAsJsonAsync(Url(s), input); }
        async Task Change() {
            var client = s.Client(u.Admin); var p = u.Pics[0];
            var r = rename ? await client.PutAsJsonAsync(PicRosterTests.Root + "/" + p.PicId, new PicEditInput(Guid.NewGuid(), 1, "Renamed concurrently", "New concurrent department"))
                : await client.PostAsJsonAsync(PicRosterTests.Root + $"/{p.PicId}/deactivate", new PicRevisionInput(Guid.NewGuid(), 1));
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        await Race(f, selectionFirst ? "UPDATE \"SharedDrafts\"" : "INSERT INTO \"PicRosterRevisions\"", selectionFirst ? Select : Change, selectionFirst ? Change : Select, "PicRosterEntries");
        Assert.Equal(selectionFirst || rename ? HttpStatusCode.OK : HttpStatusCode.Conflict, selection!.StatusCode);
        Assert.Equal(selectionFirst || rename ? 2 : 1, await DraftEvidenceAssessmentTests.Version(s));
        Assert.Equal(2, await f.Read(db => db.PicRosterRevisions.Where(x => x.PicId == u.Pics[0].PicId).CountAsync()));
        if (selectionFirst || rename) {
            var receipt = (await selection.Content.ReadFromJsonAsync<NgPicBatchResult>())!.Operation;
            var pic = Assert.Single(receipt.SelectedPics); Assert.Equal(selectionFirst ? 1 : 2, pic.RosterRevision);
            Assert.Equal(selectionFirst ? u.Pics[0].Name : "Renamed concurrently", pic.Name);
            EqualReceipt(receipt, (await s.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(s, receipt.OperationId)))!);
        } else Assert.Contains("NgPicNotSelectable", await selection.Content.ReadAsStringAsync());
    }
    [Theory][InlineData(true, "sample")][InlineData(false, "sample")][InlineData(true, "removed")][InlineData(false, "removed")]
    [InlineData(true, "unable")][InlineData(false, "unable")][InlineData(true, "attachment")][InlineData(false, "attachment")]
    [InlineData(true, "binding")][InlineData(false, "binding")][InlineData(true, "header")][InlineData(false, "header")]
    [InlineData(true, "pic")][InlineData(false, "pic")]
    public async Task All_draft_domains_contend_on_the_same_actual_Task(bool first, string domain) { await using var p = await PhotoFixture.Create(); await Cas(p, first, domain); }
    internal static async Task Cas(PhotoFixture p, bool picFirst, string domain) {
        var f = p.F; var u = await Setup(f); var s = u.S; var sourceB = u.SourceB;
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s with { Source = sourceB });
        var binding = new BindingSetup(s, u.Sample!, photo, reference!); var input = Input(u, 2); var other = s with { Source = sourceB };
        HttpResponseMessage? one = null, two = null;
        async Task Pic() { one = await s.Client(s.A).PostAsJsonAsync(Url(s), input); }
        async Task Other() { var client = s.Client(s.B); two = domain switch {
            "sample" => await client.PostAsJsonAsync(s.Root + "/operation-batches", other.Batch(other.Capture(new("OK", null, null), version: 2), 2)),
            "removed" => await client.PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(other, 2)),
            "unable" => await client.PostAsJsonAsync(UnableToMeasureTests.Url(s), UnableToMeasureTests.Input(new(s, photo, reference!), 2, sourceB)),
            "attachment" => await client.PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, photo, 2, sourceB, reference, "DetachAttachment")),
            "binding" => await client.PostAsJsonAsync(SampleEvidenceTests.Url(s), SampleEvidenceTests.Input(binding, 2, sourceB)),
            "header" => await client.PostAsJsonAsync(BasicInformationFixture.WriteUrl(s), BasicInformationFixture.Input(s, 2, source: sourceB)),
            _ => await client.PostAsJsonAsync(Url(s), Input(u, 2, [], source: sourceB)) };
        }
        // Different uploader/source accounts ensure PostgreSQL contention reaches the Task row.
        await Race(f, "UPDATE \"SharedDrafts\"", picFirst ? Pic : Other, picFirst ? Other : Pic, "PatrolTasks");
        Assert.Equal(picFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, one!.StatusCode); Assert.Equal(picFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, two!.StatusCode);
        Assert.Contains("DraftVersionConflict", await (picFirst ? two : one).Content.ReadAsStringAsync());
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(3, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(3, await f.Read(db => db.SharedAcceptanceAudits.CountAsync())); Assert.Equal(picFirst || domain == "pic" ? 1 : 0, await f.Read(db => db.NgPicSelectionOperations.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync()));
    }
    [Theory][InlineData(true, "reset")][InlineData(false, "reset")][InlineData(true, "role")][InlineData(false, "role")]
    [InlineData(true, "deactivate")][InlineData(false, "deactivate")][InlineData(true, "explicit")][InlineData(false, "explicit")]
    public async Task Source_revocation_and_acceptance_have_a_real_account_frontier(bool first, string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, first, action); }
    internal static async Task Security(PersonalSessionFixture f, bool revokeFirst, string action) {
        var u = await Setup(f); var s = u.S; var input = Input(u); var admin = u.Admin; HttpResponseMessage? accepted = null;
        var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()));
        async Task Select() { accepted = await s.Client().PostAsJsonAsync(Url(s), input); }
        async Task Revoke() {
            var r = action switch {
                "explicit" => await s.Client(admin).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Synthetic PIC source race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{s.A.User.Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{s.A.User.Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
                _ => await legacy.PostAsync($"/api/users/{s.A.User.Id}/deactivate", null) }; Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        await Race(f, revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"NgPicSelectionOperations\"", revokeFirst ? Revoke : Select, revokeFirst ? Select : Revoke, "ApplicationUsers");
        Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, accepted!.StatusCode);
        Assert.Equal(revokeFirst ? 1 : 2, await DraftEvidenceAssessmentTests.Version(s));
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        if (!revokeFirst) Assert.True((await Accept(u, input with { SourceProof = null })).ReplayOnly);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_B_logout_serializes_without_revoking_A(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await Logout(f, first); }
    internal static async Task Logout(PersonalSessionFixture f, bool logoutFirst) {
        var u = await Setup(f); var s = u.S; HttpResponseMessage? accepted = null;
        async Task Select() { accepted = await s.Client().PostAsJsonAsync(Url(s), Input(u)); }
        async Task Close() { Assert.Equal(HttpStatusCode.NoContent, (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).StatusCode); }
        await Race(f, logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"NgPicSelectionOperations\"", logoutFirst ? Close : Select, logoutFirst ? Select : Close, "ApplicationUsers");
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, accepted!.StatusCode);
        Assert.Null(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        Assert.Equal(logoutFirst ? 1 : 2, await DraftEvidenceAssessmentTests.Version(s));
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Read_and_correction_observe_one_version_in_both_orders(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await ReadRace(f, first); }
    internal static async Task ReadRace(PersonalSessionFixture f, bool readFirst) {
        var u = await Setup(f); var original = (await Accept(u, Input(u))).Operation; NgPicReadOutput? read = null;
        async Task Get() { read = await Read(u); }
        async Task Write() { await Accept(u, Input(u, 2, [], original), u.S.A); }
        await Race(f, readFirst ? "FROM \"NgPicSelectionProjections\"" : "UPDATE \"SharedDrafts\"", readFirst ? Get : Write, readFirst ? Write : Get, "PatrolTasks");
        Assert.Equal(readFirst ? 2 : 3, read!.DraftVersion); Assert.Equal(readFirst ? 1 : 2, read.History.Count);
        Assert.Equal(readFirst ? 1 : 0, read.Items.Single(i => i.TaskItemId == u.S.ItemId).LastSelection!.Receipt.SelectedPics.Count);
        Assert.Equal(3, (await Read(u)).DraftVersion);
    }
}
