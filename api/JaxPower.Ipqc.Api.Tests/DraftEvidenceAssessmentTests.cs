using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftEvidenceAssessmentTests
{
    internal static string Url(DraftSetup s) => s.Root + "/shared-draft/evidence-assessment";
    internal static async Task<long> Version(DraftSetup s) => await s.F.Read(async db => (await db.SharedDrafts.SingleOrDefaultAsync(x => x.TaskId == s.TaskId))?.DraftVersion ?? 0);
    internal static async Task<DraftEvidenceAssessmentOutput> Read(DraftSetup s, string query = "") {
        var response = await s.Client().GetAsync(Url(s) + query); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var r = (await response.Content.ReadFromJsonAsync<DraftEvidenceAssessmentOutput>())!;
        Assert.False(r.CanExecute); Assert.False(r.AcceptsFormalInspection); Assert.Equal("NotEvaluatedForFormalSubmission", r.Completeness); return r;
    }
    internal static async Task<DraftEvidenceItem> Item(DraftSetup s) => (await Read(s)).Items.Single(x => x.TaskItemId == s.ItemId);
    internal static async Task<DraftOperationReceipt> Sample(DraftSetup s, string judgment = "NG", decimal? value = null) {
        var v = await Version(s); return Assert.Single((await s.Accept(s.Batch(s.Capture(new(value is null ? judgment : null, value, null), version: v), v))).Operations);
    }
    internal static async Task<(AttachmentOutput Photo, DraftAttachmentReceipt? Ref)> Photo(DraftSetup s, bool associate = true, bool ready = true) {
        var bytes = PhotoFixture.Image(); var a = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes));
        if (ready) a = await PhotoFixture.Ready(s, a.AttachmentId, bytes);
        return (a, associate ? Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, await Version(s)))).Operations) : null);
    }
    internal static async Task<string> Facts(PhotoFixture p) => await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true) +
        await p.F.Read(async db => JsonSerializer.Serialize(new { Unable = await db.UnableToMeasureOperations.OrderBy(x => x.OperationId).ToListAsync(),
            Projections = await db.UnableToMeasureProjections.OrderBy(x => x.LastOperationId).ToListAsync(), Keys = await db.AcceptedOperationIdentities.OrderBy(x => x.OperationId).ToListAsync() }));
    [Fact] public async Task Genuine_first_NG_evidence_preserves_provenance_and_every_fact() { await using var p = await PhotoFixture.Create(); await Genuine(p); }
    internal static async Task Genuine(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F, sampleCount: "5"); var capture = await Sample(s); var (a, _) = await Photo(s, associate: false);
        var referenceSource = await WorkContextFixture.Issue(p.F, s.TaskId, s.B);
        var reference = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, await Version(s), source: referenceSource), s.A)).Operations);
        var before = await Facts(p); var r = await Read(s); var i = r.Items.Single(x => x.TaskItemId == s.ItemId);
        Assert.Equal("Satisfied", i.EvidenceStatus); Assert.Equal("ObservedNG", i.ResultState); Assert.Contains("CurrentAttemptNGPhoto", i.RequiredEvidenceCategories);
        Assert.Equal(5, i.MinimumSampleCount); Assert.False(i.MeetsMinimum); Assert.Equal(1, i.ActiveSampleCount); // Evidence-only success is not complete inspection.
        Assert.Equal(capture, Assert.Single(i.Samples).ValueOperation); var photo = Assert.Single(i.Photos); Assert.True(photo.IsEligibleEvidence); Assert.False(photo.BindsSampleTransition);
        Assert.Equal("Verified", photo.Integrity); Assert.Equal(reference, photo.LastReference); Assert.Equal(s.A.User.Id, photo.PhotoContributorUserId); Assert.Equal(s.B.User.Id, photo.PhotoUploadedByUserId);
        Assert.Equal(s.B.User.Id, reference.ContributorUserId); Assert.Equal(s.A.User.Id, reference.ActorUserId);
        Assert.Equal(s.A.PersonalSessionId, reference.ActorPersonalSessionId); Assert.Equal(s.B.PersonalSessionId, photo.PhotoUploadPersonalSessionId);
        Assert.Contains(capture.OperationId, photo.ReferenceAcceptedAfterOperationIds);
        Assert.Equal(before, await Facts(p)); var json = JsonSerializer.Serialize(r);
        foreach (var secret in new[] { s.Source.ProofSecret, s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, p.DirectoryPath }) Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("StorageKey", json); Assert.All(i.Transitions, x => Assert.False(x.IsFormalEvent));
    }
    [Theory][InlineData("none")][InlineData("ready-unassociated")][InlineData("pending")][InlineData("detached")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")]
    public async Task Missing_and_invalid_photos_never_satisfy(string kind) { await using var p = await PhotoFixture.Create(); await Availability(p, kind); }
    internal static async Task Availability(PhotoFixture p, string kind) {
        var s = await SharedDraftFixture.Setup(p.F); await Sample(s);
        if (kind != "none") {
            var (a, reference) = await Photo(s, kind is not ("ready-unassociated" or "pending"), kind != "pending");
            if (kind == "detached") await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, await Version(s), previous: reference, kind: "DetachAttachment"));
            if (kind == "missing") File.Delete(await p.FinalPath(a.AttachmentId));
            if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(a.AttachmentId), [1, 2, 3]);
            if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(q => q.SetProperty(x => x.State, "MissingUnexpected")));
        }
        var before = await Facts(p); var i = await Item(s);
        Assert.Equal(kind is "none" or "ready-unassociated" or "pending" ? "MissingEvidence" : "InvalidEvidence", i.EvidenceStatus);
        Assert.DoesNotContain(i.Photos, x => x.IsEligibleEvidence); Assert.Equal(before, await Facts(p));
        if (kind == "ready-unassociated") { Assert.Equal("Ready", Assert.Single(i.Photos).Availability); Assert.Equal("NotCheckedUnassociated", i.Photos[0].Integrity); Assert.Null(i.Photos[0].LastReference); }
    }
    [Theory][InlineData("Required")][InlineData("Optional")][InlineData("optional")][InlineData("")][InlineData("Legacy photo needed")]
    public async Task Frozen_free_text_is_never_unconfirmed_optional(string raw) { await using var p = await PhotoFixture.Create(); await Policy(p, raw); }
    internal static async Task Policy(PhotoFixture p, string raw) {
        var s = await SharedDraftFixture.Setup(p.F, photoRequirement: raw); await Sample(s, "OK"); await Photo(s);
        // Mutable today's Standard cannot reinterpret the actual frozen policy.
        await p.F.Edit(async db => { foreach (var row in await db.PatrolStandardItems.ToListAsync()) row.PhotoRequirement = "Optional"; });
        var before = await Facts(p); var i = await Item(s);
        Assert.Equal(raw, i.PhotoPolicy.FrozenText); Assert.False(i.PhotoPolicy.HasExplicitConfirmation); Assert.Equal("PolicyConfigurationUnresolved", i.PhotoPolicy.State);
        Assert.Equal("PolicyConfigurationUnresolved", i.EvidenceStatus); Assert.Contains("ConfirmedRequiredOrOptionalPolicy", i.UnprovenFacts); Assert.Equal(before, await Facts(p));
    }
    [Theory][InlineData("NG", "OK")][InlineData("NG", "NG")][InlineData("OK", "NG")]
    public async Task Exact_draft_transitions_do_not_invent_photo_binding(string from, string to) { await using var p = await PhotoFixture.Create(); await Transition(p, from, to); }
    internal static async Task Transition(PhotoFixture p, string from, string to) {
        var s = await SharedDraftFixture.Setup(p.F); var first = await Sample(s, from);
        var b = s.Mutation(first, "CorrectSample", new(to, null, null)); var second = (await s.Accept(b)).Operations.Single(x => x.OperationId == b.Operations[0].OperationId);
        await Photo(s); // Later shared version is ordering proof only, not an improvement/repeat association.
        var before = await Facts(p); var i = await Item(s); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
        var t = i.Transitions.Single(x => x.OperationId == second.OperationId); Assert.Equal(first.OperationId, t.Predecessor!.OperationId);
        Assert.Equal(from, t.FromJudgment); Assert.Equal(to, t.ToJudgment); Assert.Equal(1, t.DependencyDepth); Assert.False(t.IsFormalEvent);
        Assert.Contains(second.OperationId, Assert.Single(i.Photos).ReferenceAcceptedAfterOperationIds); Assert.False(i.Photos[0].BindsSampleTransition);
        Assert.Contains("NewEvidenceForEachRelevantTransition", i.UnprovenFacts); Assert.Equal(before, await Facts(p));
    }
    [Fact] public async Task Within_batch_dependency_not_GUID_sort_proves_transition() { await using var p = await PhotoFixture.Create(); await WithinBatch(p); }
    internal static async Task WithinBatch(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var capture = s.Capture(new("NG", null, null)) with { OperationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
        var b = s.Batch(capture); var hash = DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId, b, capture));
        var correction = capture with { OperationId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Kind = "CorrectSample", Data = new("OK", null, null), Dependencies = [new(capture.OperationId, hash)] };
        await s.Accept(b with { Operations = [correction, capture] }); await Photo(s);
        var i = await Item(s); var t = i.Transitions.Single(x => x.OperationId == correction.OperationId);
        Assert.Equal("NGToOKCorrection", t.Classification); Assert.Equal(1, t.DependencyDepth); Assert.All(i.Transitions, x => Assert.Equal(1, x.DraftVersion));
        Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
    }
    [Fact] public async Task Removing_NG_does_not_fabricate_improvement_or_erase_repeat_obligation() { await using var p = await PhotoFixture.Create(); await RemovedSample(p); }
    internal static async Task RemovedSample(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var ng = await Sample(s); await s.Accept(s.Mutation(ng, "RemoveSample", null));
        await Sample(s); await Photo(s); var i = await Item(s); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
        Assert.Equal(2, i.Samples.Count); Assert.Single(i.Samples, x => x.IsRemoved); Assert.Contains(i.Transitions, x => x.Kind == "RemoveSample" && x.FromJudgment == "NG");
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Multiple_actual_samples_use_actual_type_and_frozen_minimum(bool quantitative) { await using var p = await PhotoFixture.Create(); await Multiple(p, quantitative); }
    internal static async Task Multiple(PhotoFixture p, bool quantitative) {
        var s = await SharedDraftFixture.Setup(p.F, quantitative, "3");
        await Sample(s, "NG", quantitative ? 3m : null); await Sample(s, "OK", quantitative ? 2m : null); await Photo(s);
        var i = await Item(s); Assert.Equal(2, i.ActiveSampleCount); Assert.Equal(3, i.MinimumSampleCount); Assert.False(i.MeetsMinimum);
        Assert.Equal("ObservedNG", i.ResultState); Assert.Equal("Satisfied", i.EvidenceStatus); Assert.Equal(2, i.Transitions.Count);
        Assert.All(i.Transitions, x => Assert.Equal("InitialCapture", x.Classification));
        if (!quantitative) Assert.All(i.Samples, x => Assert.Null(x.ValueOperation!.Value!.InspectionValue));
    }
    [Theory][InlineData("item")][InlineData("task")][InlineData("stale")]
    public async Task Genuine_foreign_or_stale_evidence_cannot_substitute(string kind) { await using var p = await PhotoFixture.Create(); await Scope(p, kind); }
    internal static async Task Scope(PhotoFixture p, string kind) {
        var s = await SharedDraftFixture.Setup(p.F); await Sample(s);
        if (kind == "item") { var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId); await Photo(s with { ItemId = other.TaskItemId, AttemptId = other.InspectionAttemptId }); }
        else if (kind == "task") {
            p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(p.F));
            var task = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync());
            s = s with { A = await p.F.Login(), B = await p.F.Login("dev-ipqa-2") };
            var scope = (await s.Client().GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!.Items[0];
            var other = s with { TaskId = task, ItemId = scope.TaskItemId, AttemptId = scope.InspectionAttemptId, Source = await WorkContextFixture.Issue(p.F, task, s.A) }; await Photo(other);
        } else { await Photo(s); await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync()); }
        var i = await Item(s); Assert.Equal(kind == "stale" ? "UnsupportedUntilFutureWorkflow" : "MissingEvidence", i.EvidenceStatus);
        Assert.DoesNotContain(i.Photos, x => x.IsEligibleEvidence); if (kind == "stale") Assert.Null(i.InspectionAttemptId);
    }
    [Fact] public async Task Removed_and_Unable_remain_distinct_including_withdrawal() { await using var p = await PhotoFixture.Create(); await Dispositions(p); }
    internal static async Task Dispositions(PhotoFixture p) {
        var u = await UnableToMeasureTests.Setup(p); var s = u.S; var ng = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation;
        Assert.Equal("Satisfied", (await Item(s)).EvidenceStatus); Assert.Equal("UnableToMeasureNG", (await Item(s)).Disposition);
        await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 2, kind: "ClearUnableToMeasureNG", previous: ng));
        await Sample(s, "OK"); Assert.NotEqual("Satisfied", (await Item(s)).EvidenceStatus);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, await Version(s)));
        var i = await Item(s); Assert.Equal("Removed", i.Disposition); Assert.Equal("Satisfied", i.EvidenceStatus); Assert.Contains("NoneForRemoved", i.RequiredEvidenceCategories);
    }
    [Theory][InlineData("logout")][InlineData("reset")][InlineData("role")][InlineData("deactivate")]
    [InlineData("explicit")][InlineData("self-password")][InlineData("refresh")][InlineData("expiry")]
    public async Task Accepted_A_provenance_survives_source_lifecycle(string action) { await using var p = await PhotoFixture.Create(); await SourceLifecycle(p, action); }
    internal static async Task SourceLifecycle(PhotoFixture p, string action) {
        var s = await SharedDraftFixture.Setup(p.F); var capture = await Sample(s); var (_, r) = await Photo(s);
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        else if (action == "refresh") _ = await p.F.Refresh(s.A.RefreshToken!);
        else if (action == "expiry") { p.F.Clock.Now = p.F.Clock.Now.AddDays(15); s = s with { B = await p.F.Login("dev-ipqa-2") }; }
        else await SharedDraftFixture.Revoke(s, action);
        var before = await Facts(p); var i = await Item(s); Assert.Equal("Satisfied", i.EvidenceStatus); Assert.Equal(capture, Assert.Single(i.Samples).ValueOperation); Assert.Equal(r, Assert.Single(i.Photos).LastReference);
        Assert.Equal(before, await Facts(p));
        if (action is "explicit" or "reset" or "role" or "deactivate") { var v = await Version(s); Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", s.Batch(s.Capture(version: v), v))).StatusCode); }
    }
    [Fact] public async Task Bounded_pages_versions_and_empty_read_never_create_facts() { await using var p = await PhotoFixture.Create(); await Bounds(p); }
    internal static async Task Bounds(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var before = await Facts(p); var empty = await Read(s, "?pageSize=1&expectedDraftVersion=0");
        Assert.Equal(0, empty.DraftVersion); Assert.Equal(2, empty.TotalItems); Assert.Single(empty.Items); Assert.Equal("UnsupportedUntilFutureWorkflow", empty.Items[0].EvidenceStatus);
        var page = await Read(s, "?page=2&pageSize=1&expectedDraftVersion=0"); Assert.NotEqual(empty.Items[0].TaskItemId, Assert.Single(page.Items).TaskItemId); Assert.Empty((await Read(s, "?page=3&pageSize=1")).Items);
        Assert.Equal(before, await Facts(p)); Assert.Equal(0, await p.F.Read(db => db.SharedDrafts.CountAsync()));
        foreach (var query in new[] { "?page=0", "?pageSize=21", "?pageSize=0", "?page=2147483647&pageSize=20", "?expectedDraftVersion=-1" }) Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(Url(s) + query)).StatusCode);
        await Sample(s); Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(Url(s) + "?expectedDraftVersion=0")).StatusCode);
    }
    [Fact] public async Task Long_real_history_fails_closed_without_write_limit_or_discard() { await using var p = await PhotoFixture.Create(); await LongHistory(p); }
    internal static async Task LongHistory(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var previous = await Sample(s);
        for (var n = 0; n < 100; n++) { await using var scope = p.F.Factory.Services.CreateAsyncScope(); var batch = s.Mutation(previous, "CorrectSample", new("NG", null, null)); previous = (await scope.ServiceProvider.GetRequiredService<SharedDraftService>().AcceptAsync(s.TaskId, batch, WorkContextFixture.Principal(s.B), default)).Operations.Single(x => x.OperationId == batch.Operations[0].OperationId); }
        await Photo(s); var before = await Facts(p); var i = await Item(s); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus); Assert.False(i.SampleHistoryComplete);
        Assert.Empty(i.Transitions); Assert.Empty(i.Samples); Assert.Null(i.ActiveSampleCount); Assert.Null(i.MeetsMinimum); Assert.Equal(101, await p.F.Read(db => db.ContributionOperations.CountAsync())); Assert.Equal(before, await Facts(p));
    }
    [Fact] public async Task Current_authority_scope_and_direct_service_are_required() { await using var p = await PhotoFixture.Create(); await Authority(p); }
    internal static async Task Authority(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Anonymous.GetAsync(Url(s) + "?page=bad")).StatusCode);
        foreach (var name in new[] { "admin", "pqe" }) { var actor = await p.F.Login(name); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).GetAsync(Url(s))).StatusCode); }
        var noFamily = p.F.Token(s.B, cs => cs.Where(c => c.Type != "personal_session_id").ToList()); Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Client(noFamily).GetAsync(Url(s))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(Url(s with { TaskId = Guid.NewGuid() }))).StatusCode);
        await using var scope = p.F.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AssessEvidenceAsync(s.TaskId, new ClaimsPrincipal(new ClaimsIdentity()), default))).Status);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(Url(s))).StatusCode);
    }
}
