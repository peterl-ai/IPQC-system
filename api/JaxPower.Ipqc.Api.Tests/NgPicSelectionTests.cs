using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.NgPicSelectionFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class NgPicSelectionTests
{
    [Theory][InlineData(false)][InlineData(true)] public async Task Optional_sets_and_server_snapshots_have_real_A_B_and_no_formal_effect(bool quantitative) { await using var f = await PersonalSessionFixture.CreateAsync(); await Lifecycle(f, quantitative); }
    internal static async Task Lifecycle(PersonalSessionFixture f, bool quantitative) {
        var u = await Setup(f, quantitative); var s = u.S; var retained = await ItemRemovalTests.Retained(f);
        var taskBefore = await f.Read(db => db.PatrolTasks.Select(t => new { t.Status, t.CurrentRevisionNo, t.StartedAtUtc, t.UpdatedAtUtc, t.SubmittedAtUtc, t.OverallInspectionResult }).SingleAsync());
        var before = await Facts(f); var initial = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.False(initial.HasAcceptedSelection); Assert.Null(initial.LastSelection); Assert.Equal("OrdinaryNG", initial.CurrentNg.Kind); Assert.Equal(before, await Facts(f));
        var empty = (await Accept(u, Input(u, ids: []))).Operation; Assert.Empty(empty.SelectedPics); Assert.Equal(2, empty.DraftVersion);
        var firstRead = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId); Assert.True(firstRead.HasAcceptedSelection); Assert.Empty(firstRead.LastSelection!.Receipt.SelectedPics);
        var one = (await Accept(u, Input(u, 2, previous: empty))).Operation;
        Assert.Equal(s.A.User.Id, one.ContributorUserId); Assert.Equal(s.B.User.Id, one.ActorUserId); Assert.Equal(s.B.PersonalSessionId, one.ActorPersonalSessionId);
        var all = (await Accept(u, Input(u, 3, u.Pics.Select(p => p.PicId).Reverse(), one, u.SourceB), s.A)).Operation;
        Assert.Equal(s.B.User.Id, all.ContributorUserId); Assert.Equal(s.A.User.Id, all.ActorUserId); Assert.Equal(s.A.PersonalSessionId, all.ActorPersonalSessionId);
        Assert.Equal(u.Pics.Select(p => p.PicId).OrderBy(id => id.ToString(), StringComparer.Ordinal), all.SelectedPics.Select(p => p.PicId));
        Assert.All(all.SelectedPics, p => { var source = u.Pics.Single(x => x.PicId == p.PicId); Assert.Equal(source.Name, p.Name); Assert.Equal(source.Department, p.Department); Assert.Equal(source.Revision, p.RosterRevision); });
        Assert.Equal("OrdinaryNG", all.NgObservation.Kind); Assert.Equal(u.Sample!.OperationId, Assert.Single(all.NgObservation.References).OperationId);
        var cleared = (await Accept(u, Input(u, 4, [], all))).Operation; Assert.Empty(cleared.SelectedPics);
        EqualReceipt(one, (await s.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(s, one.OperationId)))!);
        var r = await Read(u); Assert.Equal(5, r.DraftVersion); Assert.Equal(4, r.History.Count); Assert.True(r.Items.Single(i => i.TaskItemId == s.ItemId).LastSelection!.MatchesAcceptedNgBasis);
        Assert.All(r.History, x => Assert.Equal("NotEvaluatedForFormalWorkflow", x.AssignmentDisposition));
        Assert.Equal(retained, await ItemRemovalTests.Retained(f)); Assert.Equal(taskBefore, await f.Read(db => db.PatrolTasks.Select(t => new { t.Status, t.CurrentRevisionNo, t.StartedAtUtc, t.UpdatedAtUtc, t.SubmittedAtUtc, t.OverallInspectionResult }).SingleAsync()));
        Assert.Equal(1, await f.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(4, await f.Read(db => db.NgPicSelectionOperations.CountAsync())); Assert.Equal(3, await f.Read(db => db.NgPicSelectionMembers.CountAsync()));
        before = await Facts(f); var preflight = (await s.Client().GetFromJsonAsync<SubmitPreflightOutput>(SubmitPreflightTests.Url(s)))!;
        Assert.False(preflight.CanSubmit); Assert.False(preflight.CanExecute); Assert.DoesNotContain(preflight.BlockingReasons, x => x.Contains("PIC", StringComparison.OrdinalIgnoreCase)); Assert.Equal(before, await Facts(f));
        var json = JsonSerializer.Serialize(r); foreach (var secret in new[] { s.Source.ProofSecret, s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken! }) Assert.DoesNotContain(secret, json);
    }
    [Theory][InlineData("duplicate")][InlineData("unknown")][InlineData("account")][InlineData("empty-id")][InlineData("null")][InlineData("oversized")][InlineData("inactive")]
    public async Task Invalid_or_inactive_real_PIC_sets_have_no_partial_effect(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Invalid(f, kind); }
    internal static async Task Invalid(PersonalSessionFixture f, string kind) {
        var u = await Setup(f); var b = Input(u); var expected = HttpStatusCode.BadRequest;
        b = kind switch {
            "duplicate" => b with { Operation = b.Operation with { PicIds = [u.Pics[0].PicId, u.Pics[0].PicId] } },
            "unknown" => b with { Operation = b.Operation with { PicIds = [u.Pics[0].PicId, Guid.NewGuid()] } },
            "account" => b with { Operation = b.Operation with { PicIds = [TestAccounts.Ipqa1Id] } },
            "empty-id" => b with { Operation = b.Operation with { PicIds = [Guid.Empty] } },
            "null" => b with { Operation = b.Operation with { PicIds = null! } },
            "oversized" => b with { Operation = b.Operation with { PicIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList() } }, _ => b };
        if (kind == "inactive") (await f.Client(u.Admin.AccessToken).PostAsJsonAsync(PicRosterTests.Root + $"/{u.Pics[0].PicId}/deactivate", new PicRevisionInput(Guid.NewGuid(), 1))).EnsureSuccessStatusCode();
        if (kind is "unknown" or "account" or "inactive") expected = HttpStatusCode.Conflict;
        var before = await Facts(f); Assert.Equal(expected, (await u.S.Client().PostAsJsonAsync(Url(u.S), b)).StatusCode); Assert.Equal(before, await Facts(f));
        Assert.Equal(1, await DraftEvidenceAssessmentTests.Version(u.S)); Assert.Equal(0, await f.Read(db => db.NgPicSelectionOperations.CountAsync()));
    }
    [Fact] public async Task Accepted_snapshots_survive_Admin_renaming_deactivation_and_replay() { await using var f = await PersonalSessionFixture.CreateAsync(); await RosterHistory(f); }
    internal static async Task RosterHistory(PersonalSessionFixture f) {
        var u = await Setup(f); var b = Input(u); var first = (await Accept(u, b)).Operation; var admin = f.Client(u.Admin.AccessToken); var id = u.Pics[0].PicId;
        (await admin.PutAsJsonAsync(PicRosterTests.Root + "/" + id, new PicEditInput(Guid.NewGuid(), 1, "New synthetic name", "New synthetic dept"))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync(PicRosterTests.Root + $"/{id}/deactivate", new PicRevisionInput(Guid.NewGuid(), 2))).EnsureSuccessStatusCode();
        Assert.Equal(2, await DraftEvidenceAssessmentTests.Version(u.S)); var before = await Facts(f);
        EqualReceipt(first, (await Read(u)).Items.Single(i => i.TaskItemId == u.S.ItemId).LastSelection!.Receipt);
        var retry = await Accept(u, b with { SourceProof = null }); Assert.True(retry.ReplayOnly); EqualReceipt(first, retry.Operation);
        Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, 2, previous: first))).StatusCode); Assert.Equal(before, await Facts(f));
        var empty = (await Accept(u, Input(u, 2, [], first))).Operation;
        (await admin.PostAsJsonAsync(PicRosterTests.Root + $"/{id}/activate", new PicRevisionInput(Guid.NewGuid(), 3))).EnsureSuccessStatusCode();
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(u.S)); var renewed = (await Accept(u, Input(u, 3, previous: empty))).Operation;
        Assert.Equal(4, renewed.SelectedPics[0].RosterRevision); Assert.Equal("New synthetic name", renewed.SelectedPics[0].Name);
        EqualReceipt(first, (await u.S.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(u.S, first.OperationId)))!);
        Assert.Equal(u.Pics[0].Name, first.SelectedPics[0].Name); Assert.Equal(1, first.SelectedPics[0].RosterRevision);
    }
    [Fact] public async Task Corrected_OK_and_later_NG_retain_facts_without_inventing_assignment_disposition() { await using var f = await PersonalSessionFixture.CreateAsync(); await Changes(f); }
    internal static async Task Changes(PersonalSessionFixture f) {
        var u = await Setup(f); var s = u.S; var input = Input(u); var pic = (await Accept(u, input)).Operation;
        async Task<DraftOperationReceipt> Correct(DraftOperationReceipt previous, long version, string judgment) {
            var old = s.Mutation(previous, "CorrectSample", new(judgment, null, null));
            var batch = old with { ExpectedDraftVersion = version, Operations = [old.Operations[0] with { BaseDraftVersion = version }] };
            return (await s.Accept(batch)).Operations.Single(x => x.OperationId == batch.Operations[0].OperationId);
        }
        var ok = await Correct(u.Sample!, 2, "OK"); var observed = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.Equal("NotCurrentlyNG", observed.CurrentNg.Kind); Assert.True(observed.HasAcceptedSelection); Assert.Null(observed.LastSelection!.MatchesAcceptedNgBasis); EqualReceipt(pic, observed.LastSelection.Receipt);
        var before = await Facts(f); Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(u, 3, [], pic))).StatusCode); Assert.Equal(before, await Facts(f));
        Assert.True((await Accept(u, input with { SourceProof = null })).ReplayOnly);
        var ng = await Correct(ok, 3, "NG"); observed = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.Equal("OrdinaryNG", observed.CurrentNg.Kind); Assert.Equal(ng.OperationId, Assert.Single(observed.CurrentNg.References).OperationId);
        Assert.False(observed.LastSelection!.MatchesAcceptedNgBasis); Assert.Equal("NotEvaluatedForFormalWorkflow", observed.LastSelection.AssignmentDisposition); EqualReceipt(pic, observed.LastSelection.Receipt);
        var explicitNew = (await Accept(u, Input(u, 4, [], pic))).Operation; Assert.Equal(ng.OperationId, Assert.Single(explicitNew.NgObservation.References).OperationId);
        Assert.Equal(1, await f.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(1, await f.Read(db => db.InspectionAttempts.CountAsync(x => x.PatrolTaskItemId == s.ItemId)));
    }
    [Fact] public async Task Multiple_samples_share_one_item_attempt_selection_and_Removed_retains_history() { await using var f = await PersonalSessionFixture.CreateAsync(); await Multiple(f); }
    internal static async Task Multiple(PersonalSessionFixture f) {
        var u = await Setup(f); var s = u.S; var second = await DraftEvidenceAssessmentTests.Sample(s);
        var b = Input(u, 2); var pic = (await Accept(u, b)).Operation; Assert.Equal(2, pic.NgObservation.References.Count);
        var remove = s.Mutation(u.Sample!, "RemoveSample", null); remove = remove with { ExpectedDraftVersion = 3, Operations = [remove.Operations[0] with { BaseDraftVersion = 3 }] };
        await s.Accept(remove); var current = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.Equal(second.OperationId, Assert.Single(current.CurrentNg.References).OperationId); Assert.False(current.LastSelection!.MatchesAcceptedNgBasis);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 4)); current = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.Equal("Removed", current.CurrentNg.Kind); Assert.True(current.HasAcceptedSelection); EqualReceipt(pic, current.LastSelection!.Receipt);
        var before = await Facts(f); Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(u, 5, [], pic))).StatusCode); Assert.Equal(before, await Facts(f));
        Assert.True((await Accept(u, b with { SourceProof = null })).ReplayOnly); Assert.Equal(1, await f.Read(db => db.NgPicSelectionProjections.CountAsync()));
    }
    [Fact] public async Task Genuine_Unable_NG_needs_no_fake_sample_or_repeat_photo_probe() { await using var p = await PhotoFixture.Create(); await Unable(p); }
    internal static async Task Unable(PhotoFixture p) {
        var u = await Setup(p.F, sampled: false); var s = u.S; var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var disposition = (await UnableToMeasureTests.Accept(new(s, photo, reference!), UnableToMeasureTests.Input(new(s, photo, reference!)))).Operation;
        var reads = p.Fault.ReadCount; var first = (await Accept(u, Input(u, 2, []))).Operation;
        Assert.Equal("UnableToMeasureNG", first.NgObservation.Kind); Assert.Equal(disposition.OperationId, Assert.Single(first.NgObservation.References).OperationId); Assert.Equal(reads, p.Fault.ReadCount);
        Assert.Equal(0, await p.F.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.PatrolTaskItemSamples.CountAsync()));
        var clear = UnableToMeasureTests.Input(new(s, photo, reference!), 3, previous: disposition, kind: "ClearUnableToMeasureNG");
        await UnableToMeasureTests.Accept(new(s, photo, reference!), clear); var current = (await Read(u)).Items.Single(i => i.TaskItemId == s.ItemId);
        Assert.Equal("NotCurrentlyNG", current.CurrentNg.Kind); Assert.True(current.HasAcceptedSelection); EqualReceipt(first, current.LastSelection!.Receipt);
    }
    [Fact] public async Task No_NG_fact_cannot_mint_a_selection_even_when_empty() { await using var f = await PersonalSessionFixture.CreateAsync(); await NoNg(f); }
    internal static async Task NoNg(PersonalSessionFixture f) {
        var u = await Setup(f, sampled: false); var before = await Facts(f);
        foreach (var ids in new[] { new List<Guid>(), new List<Guid> { u.Pics[0].PicId } }) {
            var r = await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, 0, ids)); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("NgPicItemNotEligible", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, await Facts(f)); Assert.Equal(0, await f.Read(db => db.SharedDrafts.CountAsync())); Assert.Equal(0, await f.Read(db => db.SharedSamples.CountAsync()));
    }
    [Fact] public async Task Replay_is_set_canonical_original_and_changed_identity_conflicts() { await using var f = await PersonalSessionFixture.CreateAsync(); await Replay(f); }
    internal static async Task Replay(PersonalSessionFixture f) {
        var u = await Setup(f); var b = Input(u, ids: u.Pics.Select(p => p.PicId)); var first = await Accept(u, b); var before = await Facts(f);
        var replay = await Accept(u, b with { SourceProof = null, Operation = b.Operation with { PicIds = b.Operation.PicIds.AsEnumerable().Reverse().ToList() } }, u.S.A);
        Assert.True(replay.ReplayOnly); EqualReceipt(first.Operation, replay.Operation); Assert.Equal(JsonSerializer.Serialize(first.BatchReceipt), JsonSerializer.Serialize(replay.BatchReceipt));
        var opReplay = await Accept(u, b with { BatchId = Guid.NewGuid(), ExpectedDraftVersion = 99, SourceProof = null }); Assert.True(opReplay.ReplayOnly); Assert.Null(opReplay.BatchReceipt);
        EqualReceipt(first.Operation, opReplay.Operation);
        foreach (var bad in new[] { b with { Operation = b.Operation with { PicIds = [] } }, b with { ExpectedDraftVersion = 2 }, b with { CanonicalizationVersion = 2 },
            b with { Operation = b.Operation with { CanonicalizationVersion = 2 } }, b with { Operation = b.Operation with { SourceWorkContextId = u.SourceB.Context.Id, ContributorUserIdAssertion = u.S.B.User.Id }, SourceProof = new(u.SourceB.Context.Id, u.SourceB.ProofSecret) } })
            Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), bad)).StatusCode);
        Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Lost_committed_response_recovers_original_typed_and_common_receipts() { await using var f = await PersonalSessionFixture.CreateAsync(); await Lost(f); }
    internal static async Task Lost(PersonalSessionFixture f) {
        var u = await Setup(f); var b = Input(u); Assert.Equal(HttpStatusCode.NotFound, (await u.S.Client().GetAsync(ReceiptUrl(u.S, b.Operation.OperationId))).StatusCode);
        f.CommitFault.AfterCommit = () => throw new IOException("Synthetic unknown response after actual NG PIC commit");
        Assert.Equal(HttpStatusCode.InternalServerError, (await u.S.Client().PostAsJsonAsync(Url(u.S), b)).StatusCode);
        var before = await Facts(f); var original = (await u.S.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(u.S, b.Operation.OperationId)))!;
        var common = (await u.S.Client().GetFromJsonAsync<DraftBatchReceipt>(u.S.Root + $"/operation-batches/{b.BatchId}/receipt"))!;
        Assert.Equal(7, common.ReceiptVersion); EqualReceipt(original, Assert.Single(common.PicSelectionOperations!));
        var replay = await Accept(u, b with { SourceProof = null }, u.S.A); Assert.True(replay.ReplayOnly); EqualReceipt(original, replay.Operation); Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Cursor_history_does_not_replace_current_selection_or_mutate_any_fact() { await using var f = await PersonalSessionFixture.CreateAsync(); await Paging(f); }
    internal static async Task Paging(PersonalSessionFixture f) {
        var u = await Setup(f); var last = (await Accept(u, Input(u))).Operation;
        for (var n = 2; n <= 6; n++) last = (await Accept(u, Input(u, n, n % 2 == 0 ? [] : [u.Pics[0].PicId], last))).Operation;
        var before = await Facts(f); var first = await Read(u, "?historyLimit=2&expectedDraftVersion=7"); var second = await Read(u, "?historyLimit=2&beforeDraftVersion=" + first.HistoryBeforeDraftVersion);
        var third = await Read(u, "?historyLimit=2&beforeDraftVersion=" + second.HistoryBeforeDraftVersion);
        Assert.Equal(new long[] { 7, 6, 5, 4, 3, 2 }, first.History.Concat(second.History).Concat(third.History).Select(o => o.Receipt.DraftVersion)); Assert.Null(third.HistoryBeforeDraftVersion);
        EqualReceipt(last, second.Items.Single(i => i.TaskItemId == u.S.ItemId).LastSelection!.Receipt);
        foreach (var query in new[] { "?page=0", "?pageSize=21", "?historyLimit=0", "?historyLimit=101", "?beforeDraftVersion=0", "?expectedDraftVersion=-1" }) Assert.Equal(HttpStatusCode.BadRequest, (await u.S.Client().GetAsync(ReadUrl(u.S) + query)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().GetAsync(ReadUrl(u.S) + "?expectedDraftVersion=1")).StatusCode); Assert.Equal(before, await Facts(f));
    }
}
