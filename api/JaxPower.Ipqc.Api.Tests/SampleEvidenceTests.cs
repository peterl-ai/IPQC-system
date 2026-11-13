using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed record BindingSetup(DraftSetup S, DraftOperationReceipt Sample, AttachmentOutput Photo, DraftAttachmentReceipt Reference);
public sealed class SampleEvidenceTests
{
    internal static string Url(DraftSetup s) => s.Root + "/sample-evidence-batches";
    internal static string ReadUrl(DraftSetup s) => s.Root + "/shared-draft/evidence-bindings";
    internal static async Task<BindingSetup> Setup(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var sample = await DraftEvidenceAssessmentTests.Sample(s);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); return new(s, sample, photo, reference!);
    }
    internal static SampleEvidenceBatchInput Input(BindingSetup u, long version = 2, WorkContextIssued? source = null,
        string kind = "BindSampleEvidence", SampleEvidenceReceipt? previous = null) {
        var s = u.S; var c = source ?? s.Source; var id = Guid.NewGuid();
        return new(Guid.NewGuid(), 1, s.F.Line1, 2, s.F.Options.ValidEnvironment!, version,
            new(id, 1, kind, previous?.BindingId ?? id, s.ItemId, s.AttemptId, new(u.Sample.OperationId, u.Sample.ContentHash),
                kind == "WithdrawSampleEvidence" ? null : new(u.Reference.OperationId, u.Reference.ContentHash), c.Context.Id, c.Context.UserId, version, 1,
                previous is null ? null : new(previous.OperationId, previous.ContentHash)), new(c.Context.Id, c.ProofSecret));
    }
    internal static async Task<SampleEvidenceBatchResult> Accept(BindingSetup u, SampleEvidenceBatchInput b, AuthOutput? actor = null) {
        var r = await u.S.Client(actor).PostAsJsonAsync(Url(u.S), b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString()); var result = (await r.Content.ReadFromJsonAsync<SampleEvidenceBatchResult>())!;
        Assert.False(result.CanExecute); Assert.False(result.AcceptsFormalInspection); return result;
    }
    internal static async Task<SampleEvidenceReadOutput> Read(BindingSetup u, string query = "") =>
        (await u.S.Client().GetFromJsonAsync<SampleEvidenceReadOutput>(ReadUrl(u.S) + query))!;
    internal static async Task<SampleEvidenceBatchResult> Direct(BindingSetup u, SampleEvidenceBatchInput b) {
        // Exercise the same transactional service for long histories without bypassing HTTP's production rate limit.
        await using var scope = u.S.F.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SharedDraftService>().AcceptSampleEvidenceAsync(u.S.TaskId, b, WorkContextFixture.Principal(u.S.B), default);
    }
    [Fact] public async Task Genuine_lifecycle_preserves_original_provenance_and_exact_receipts() { await using var p = await PhotoFixture.Create(); await Lifecycle(p); }
    internal static async Task Lifecycle(PhotoFixture p) {
        var u = await Setup(p); var s = u.S; var sourceB = await WorkContextFixture.Issue(p.F, s.TaskId, s.B);
        var b = Input(u, source: sourceB); var original = await Accept(u, b, s.A); var r = original.Operation;
        Assert.Equal("InitialNGCapture", r.Transition.Classification); Assert.Equal(u.Sample, r.Transition.Operation); Assert.Null(r.Transition.Predecessor);
        Assert.Equal(u.Reference, r.PhotoReference); Assert.Equal(s.B.User.Id, r.ContributorUserId); Assert.Equal(s.A.User.Id, r.ActorUserId);
        Assert.True(r.AssociationFactOnly); Assert.False(r.ProvesCoverage); Assert.False(r.ProvesNewEvidence); Assert.False(r.ProvesPhysicalCaptureTime);
        var batchJson = await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt");
        Assert.Equal(5, original.BatchReceipt!.ReceiptVersion);
        var again = await Accept(u, b with { SourceProof = null }); Assert.True(again.ReplayOnly); Assert.Equal(r, again.Operation);
        var opOnly = await Accept(u, b with { BatchId = Guid.NewGuid(), SourceProof = null }); Assert.True(opOnly.ReplayOnly); Assert.Null(opOnly.BatchReceipt); Assert.Equal(r, opOnly.Operation);
        Assert.Equal(r, await s.Client().GetFromJsonAsync<SampleEvidenceReceipt>(s.Root + $"/operations/{r.OperationId}/receipt"));
        var (_, photo2) = await DraftEvidenceAssessmentTests.Photo(s); u = u with { Reference = photo2! };
        var corrected = (await Accept(u, Input(u, 4, kind: "CorrectSampleEvidence", previous: r))).Operation;
        Assert.Equal(r.BindingId, corrected.BindingId); Assert.Equal(r.OperationId, corrected.Predecessor!.OperationId);
        var withdrawn = (await Accept(u, Input(u, 5, kind: "WithdrawSampleEvidence", previous: corrected))).Operation;
        Assert.False(withdrawn.IsActive); Assert.Null(withdrawn.PhotoReference);
        var read = await Read(u); Assert.Equal(6, read.DraftVersion); Assert.Equal(3, read.History.Count);
        Assert.Equal("Withdrawn", Assert.Single(read.Bindings).State); Assert.False(read.CanExecute); Assert.False(read.AcceptsFormalInspection);
        Assert.Equal(batchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt"));
        Assert.Equal(1, await p.F.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(2, await p.F.Read(db => db.DraftAttachmentProjections.CountAsync(x => x.IsAttached)));
        Assert.Equal(0, await p.F.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(3, await p.F.Read(db => db.AcceptedOperationIdentities.CountAsync(x => x.Domain == "SampleTransitionEvidence")));
        foreach (var secret in new[] { s.Source.ProofSecret, sourceB.ProofSecret, s.A.AccessToken, s.B.AccessToken, p.DirectoryPath }) Assert.DoesNotContain(secret, batchJson);
        Assert.DoesNotContain("StorageKey", batchJson);
    }
    [Theory][InlineData("NG", "OK")][InlineData("NG", "NG")][InlineData("OK", "NG")]
    public async Task Exact_accepted_dependency_classifies_without_claiming_new_evidence(string from, string to) { await using var p = await PhotoFixture.Create(); await Transition(p, from, to); }
    internal static async Task Transition(PhotoFixture p, string from, string to) {
        var s = await SharedDraftFixture.Setup(p.F); var first = await DraftEvidenceAssessmentTests.Sample(s, from);
        var correctionBatch = s.Mutation(first, "CorrectSample", new(to, null, null));
        var correction = (await s.Accept(correctionBatch)).Operations.Single(x => x.OperationId == correctionBatch.Operations[0].OperationId);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); var u = new BindingSetup(s, correction, photo, reference!);
        var r = (await Accept(u, Input(u, 3))).Operation;
        Assert.Equal(from + "To" + to + "Correction", r.Transition.Classification); Assert.Equal(first, r.Transition.Predecessor); Assert.Equal(correction, r.Transition.Operation);
        var before = await Facts(p); var i = await DraftEvidenceAssessmentTests.Item(s);
        Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus); Assert.Contains("NewEvidenceOrCoverageSemanticsUnresolved", i.Reasons);
        Assert.Contains("NewEvidenceForEachRelevantTransition", i.UnprovenFacts); Assert.True(Assert.Single(i.Photos).BindsSampleTransition);
        Assert.Equal(r, Assert.Single(i.AcceptedBindings!).LastOperation); Assert.True(i.BindingFactsComplete); Assert.Equal(before, await Facts(p));
        // A subsequent real correction remains writable, and does not borrow this binding's target identity.
        var change = s.Mutation(correction, "CorrectSample", new("NG", null, null));
        await s.Accept(change with { ExpectedDraftVersion = 4, Operations = [change.Operations[0] with { BaseDraftVersion = 4 }] });
        var read = Assert.Single((await Read(u)).Bindings); Assert.False(read.IsCurrentSampleValue); Assert.True(read.IsCurrentPhotoEligible);
        i = await DraftEvidenceAssessmentTests.Item(s); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
        Assert.Contains("PhotoToInspectionTransitionAssociationNotRecorded", i.Reasons);
    }
    internal static async Task<string> Facts(PhotoFixture p) => await DraftEvidenceAssessmentTests.Facts(p) + await p.F.Read(async db => JsonSerializer.Serialize(new {
        Operations = await db.SampleEvidenceOperations.OrderBy(x => x.OperationId).ToListAsync(), Projections = await db.SampleEvidenceProjections.OrderBy(x => x.BindingId).ToListAsync() }));
    [Theory][InlineData("unaccepted")][InlineData("hash")][InlineData("item")][InlineData("attempt")][InlineData("task")]
    [InlineData("line")][InlineData("environment")][InlineData("workflow")][InlineData("stale")][InlineData("removed")][InlineData("unable")]
    public async Task Exact_target_scope_is_required(string kind) { await using var p = await PhotoFixture.Create(); await Scope(p, kind); }
    internal static async Task Scope(PhotoFixture p, string kind) {
        var u = await Setup(p); var s = u.S; var b = Input(u); var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        b = kind switch {
            "unaccepted" => b with { Operation = b.Operation with { SampleOperation = new(Guid.NewGuid(), u.Sample.ContentHash) } },
            "hash" => b with { Operation = b.Operation with { SampleOperation = new(u.Sample.OperationId, new string('A', 64)) } },
            "item" => b with { Operation = b.Operation with { TaskItemId = other.TaskItemId, InspectionAttemptId = other.InspectionAttemptId } },
            "attempt" => b with { Operation = b.Operation with { InspectionAttemptId = other.InspectionAttemptId } },
            "line" => b with { ExpectedTaskLineId = p.F.Line2 }, "environment" => b with { ExpectedEnvironmentId = "other" }, "workflow" => b with { ExpectedWorkflowVersion = 1 }, _ => b };
        if (kind == "stale") await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync());
        if (kind == "removed") { await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 2)); b = b with { ExpectedDraftVersion = 3, Operation = b.Operation with { BaseDraftVersion = 3 } }; }
        if (kind == "unable") { await UnableToMeasureTests.Accept(new(s, u.Photo, u.Reference), UnableToMeasureTests.Input(new(s, u.Photo, u.Reference), 2)); b = b with { ExpectedDraftVersion = 3, Operation = b.Operation with { BaseDraftVersion = 3 } }; }
        if (kind == "task") {
            p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(p.F));
            var task = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync()); s = s with { TaskId = task, B = await p.F.Login("dev-ipqa-2") };
        }
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        Assert.Equal(0, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync()));
    }
    [Theory][InlineData("unassociated")][InlineData("pending")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")]
    [InlineData("detached")][InlineData("superseded")][InlineData("hash")][InlineData("unaccepted")][InlineData("other-item")]
    public async Task Genuine_current_Ready_photo_reference_is_required(string kind) { await using var p = await PhotoFixture.Create(); await Photo(p, kind); }
    internal static async Task Photo(PhotoFixture p, string kind) {
        var u = await Setup(p); var s = u.S; var reference = new DraftOperationReference(u.Reference.OperationId, u.Reference.ContentHash);
        if (kind == "missing") File.Delete(await p.FinalPath(u.Photo.AttachmentId));
        if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(u.Photo.AttachmentId), [1, 2, 3]);
        if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(v => v.SetProperty(x => x.State, "Corrupt")));
        if (kind is "detached" or "superseded") {
            var detach = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, u.Photo, 2, previous: u.Reference, kind: "DetachAttachment"))).Operations);
            if (kind == "superseded") await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, u.Photo, 3, previous: detach));
        }
        if (kind == "hash") reference = reference with { ContentHash = new string('A', 64) };
        if (kind is "unassociated" or "pending" or "unaccepted") {
            var (photo, _) = await DraftEvidenceAssessmentTests.Photo(s, associate: false, ready: kind != "pending");
            reference = new(kind == "unaccepted" ? photo.AttachmentId : Guid.NewGuid(), u.Reference.ContentHash);
        }
        if (kind == "other-item") {
            var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
            var (_, r) = await DraftEvidenceAssessmentTests.Photo(s with { ItemId = other.TaskItemId, AttemptId = other.InspectionAttemptId }); reference = new(r!.OperationId, r.ContentHash);
        }
        var b = Input(u, await DraftEvidenceAssessmentTests.Version(s)); b = b with { Operation = b.Operation with { PhotoReference = reference } };
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        Assert.Equal(0, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync()));
    }
    [Theory][InlineData("OK-capture")][InlineData("OK-OK")][InlineData("remove")]
    public async Task Unsupported_operations_are_not_invented_transitions(string kind) { await using var p = await PhotoFixture.Create(); await Unsupported(p, kind); }
    internal static async Task Unsupported(PhotoFixture p, string kind) {
        var s = await SharedDraftFixture.Setup(p.F); var r = await DraftEvidenceAssessmentTests.Sample(s, "OK");
        if (kind != "OK-capture") {
            var batch = s.Mutation(r, kind == "remove" ? "RemoveSample" : "CorrectSample", kind == "remove" ? null : new("OK", null, null));
            r = (await s.Accept(batch)).Operations.Single(x => x.OperationId == batch.Operations[0].OperationId);
        }
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); var u = new BindingSetup(s, r, photo, reference!);
        var response = await s.Client().PostAsJsonAsync(Url(s), Input(u, await DraftEvidenceAssessmentTests.Version(s)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("BindingSampleTransitionUnsupported", await response.Content.ReadAsStringAsync());
    }
    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("self-password")][InlineData("refresh")]
    [InlineData("explicit")][InlineData("reset")][InlineData("role")][InlineData("deactivate")]
    public async Task Accepted_old_sources_remain_facts_but_fresh_binding_source_is_independent(string action) { await using var p = await PhotoFixture.Create(); await Source(p, action); }
    internal static async Task Source(PhotoFixture p, string action) {
        var u = await Setup(p); var s = u.S;
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        else if (action == "refresh") _ = await p.F.Refresh(s.A.RefreshToken!);
        else if (action == "expiry") { p.F.Clock.Now = p.F.Clock.Now.AddDays(15); s = s with { B = await p.F.Login("dev-ipqa-2") }; u = u with { S = s }; }
        else await SharedDraftFixture.Revoke(s, action);
        if (action is "explicit" or "reset" or "role" or "deactivate") {
            var bad = await s.Client().PostAsJsonAsync(Url(s), Input(u)); Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
            var source = await WorkContextFixture.Issue(p.F, s.TaskId, s.B);
            var r = (await Accept(u, Input(u, source: source))).Operation; Assert.Equal(u.Sample, r.Transition.Operation); Assert.Equal(u.Reference, r.PhotoReference);
        } else {
            Assert.Null(await p.F.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
            var b = Input(u); var r = (await Accept(u, b)).Operation; Assert.Equal(s.A.User.Id, r.ContributorUserId); Assert.Equal(s.B.User.Id, r.ActorUserId);
        }
    }
    [Fact] public async Task Amendments_require_exact_current_predecessor_and_cannot_retarget() { await using var p = await PhotoFixture.Create(); await Amendments(p); }
    internal static async Task Amendments(PhotoFixture p) {
        var u = await Setup(p); var first = (await Accept(u, Input(u))).Operation;
        var secondSample = await DraftEvidenceAssessmentTests.Sample(u.S); var alternate = u with { Sample = secondSample };
        foreach (var b in new[] { Input(alternate, 4, kind: "CorrectSampleEvidence", previous: first), Input(u, 4, kind: "CorrectSampleEvidence"),
            Input(u, 4, kind: "CorrectSampleEvidence", previous: first) with { Operation = Input(u, 4, kind: "CorrectSampleEvidence", previous: first).Operation with { Predecessor = new(first.OperationId, new string('A', 64)) } } })
            Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), b)).StatusCode);
        File.Delete(await p.FinalPath(u.Photo.AttachmentId));
        var withdrawn = (await Accept(u, Input(u, 4, kind: "WithdrawSampleEvidence", previous: first))).Operation;
        Assert.False(withdrawn.IsActive); Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, 5, kind: "CorrectSampleEvidence", previous: withdrawn))).StatusCode);
        Assert.Equal(2, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync()));
    }
    [Theory][InlineData("removed")][InlineData("unable")][InlineData("detach")]
    public async Task Later_disposition_or_detach_does_not_erase_receipt_and_withdrawal_remains_explicit(string action) { await using var p = await PhotoFixture.Create(); await Later(p, action); }
    internal static async Task Later(PhotoFixture p, string action) {
        var u = await Setup(p); var b = Input(u); var r = (await Accept(u, b)).Operation;
        if (action == "removed") await ItemRemovalTests.Accept(u.S, ItemRemovalTests.Input(u.S, 3));
        else if (action == "unable") await UnableToMeasureTests.Accept(new(u.S, u.Photo, u.Reference), UnableToMeasureTests.Input(new(u.S, u.Photo, u.Reference), 3));
        else await DraftAttachmentTests.Accept(u.S, DraftAttachmentTests.Input(u.S, u.Photo, 3, previous: u.Reference, kind: "DetachAttachment"));
        Assert.False(Assert.Single((await Read(u)).Bindings).IsCurrentPhotoEligible); Assert.Equal(r, (await Accept(u, b with { SourceProof = null })).Operation);
        await Accept(u, Input(u, 4, kind: "WithdrawSampleEvidence", previous: r)); Assert.Equal("Withdrawn", Assert.Single((await Read(u)).Bindings).State);
    }
    [Fact] public async Task Canonical_identity_replay_and_cross_domain_exclusion() { await using var p = await PhotoFixture.Create(); await Identities(p); }
    internal static async Task Identities(PhotoFixture p) {
        var u = await Setup(p); var s = u.S; var b = Input(u); var r = (await Accept(u, b)).Operation;
        var lower = b with { Operation = b.Operation with { SampleOperation = b.Operation.SampleOperation with { ContentHash = b.Operation.SampleOperation.ContentHash.ToLowerInvariant() }, PhotoReference = b.Operation.PhotoReference! with { ContentHash = b.Operation.PhotoReference.ContentHash.ToLowerInvariant() } } };
        Assert.Equal(r, (await Accept(u, lower with { SourceProof = null })).Operation);
        var sourceB = await WorkContextFixture.Issue(p.F, s.TaskId, s.B);
        foreach (var changed in new[] {
            b with { CanonicalizationVersion = 2 }, b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { CanonicalizationVersion = 2 } },
            b with { Operation = b.Operation with { OriginLocalEditVersion = 2 } }, b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { BaseDraftVersion = 3 } },
            b with { BatchId = Guid.NewGuid(), SourceProof = new(sourceB.Context.Id, sourceB.ProofSecret), Operation = b.Operation with { SourceWorkContextId = sourceB.Context.Id, ContributorUserIdAssertion = sourceB.Context.UserId } },
            b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { BindingId = Guid.NewGuid() } } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), changed)).StatusCode);
        foreach (var known in new[] { u.Sample.OperationId, u.Reference.OperationId }) {
            var reuse = Input(u, 3); reuse = reuse with { Operation = reuse.Operation with { OperationId = known, BindingId = known } };
            var response = await s.Client().PostAsJsonAsync(Url(s), reuse); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
        }
        var sample = s.Batch(s.Capture(version: 3) with { OperationId = r.OperationId }, 3);
        var item = ItemRemovalTests.Input(s, 3); item = item with { Operation = item.Operation with { OperationId = r.OperationId } };
        var photo = DraftAttachmentTests.Input(s, u.Photo, 3, previous: u.Reference, kind: "DetachAttachment"); photo = photo with { Operations = [photo.Operations[0] with { OperationId = r.OperationId }] };
        var unable = UnableToMeasureTests.Input(new(s, u.Photo, u.Reference), 3); unable = unable with { Operation = unable.Operation with { OperationId = r.OperationId } };
        foreach (var response in new[] { await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sample), await s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), item),
            await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), photo), await s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), unable) }) {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(3, await DraftEvidenceAssessmentTests.Version(s));
    }
    [Fact] public async Task Fifty_real_samples_can_share_explicit_photo_facts_without_claiming_coverage() { await using var p = await PhotoFixture.Create(); await Capacity(p); }
    internal static async Task Capacity(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F, sampleCount: "50"); var first = s.Capture(new("NG", null, null));
        var batch = s.Batch(first) with { Operations = Enumerable.Range(0, 50).Select(_ => s.Capture(new("NG", null, null))).ToList() };
        var samples = (await s.Accept(batch)).Operations; Assert.Equal(50, samples.Count);
        var photos = new List<(AttachmentOutput Photo, DraftAttachmentReceipt Ref)>();
        for (var n = 0; n < 5; n++) { var (a, r) = await DraftEvidenceAssessmentTests.Photo(s); photos.Add((a, r!)); }
        var version = 6L; SampleEvidenceReceipt? initial = null; BindingSetup? u = null;
        foreach (var sample in samples) {
            u = new(s, sample, photos[0].Photo, photos[0].Ref); var r = (await Direct(u, Input(u, version++))).Operation;
            initial ??= r; Assert.False(r.ProvesCoverage); Assert.False(r.ProvesNewEvidence);
        }
        Assert.Equal(50, (await Read(u!)).TotalBindings); Assert.Equal(50, await p.F.Read(db => db.SharedSampleProjections.CountAsync(x => !x.IsRemoved)));
        // Correction/withdrawal never detaches a photo or returns its reserved slot.
        var firstU = new BindingSetup(s, samples[0], photos[0].Photo, photos[1].Ref);
        var corrected = (await Direct(firstU, Input(firstU, version++, kind: "CorrectSampleEvidence", previous: initial))).Operation;
        await Direct(firstU, Input(firstU, version++, kind: "WithdrawSampleEvidence", previous: corrected));
        Assert.Equal(5, await p.F.Read(db => db.DraftAttachmentProjections.CountAsync(x => x.IsAttached)));
        var bytes = PhotoFixture.Image(); var denied = await s.Client().PostAsJsonAsync(PhotoFixture.Root(s), PhotoFixture.Input(s, bytes));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); Assert.Equal(5, await p.F.Read(db => db.InspectionAttachments.CountAsync()));
        Assert.Equal(50, (await DraftEvidenceAssessmentTests.Item(s)).MinimumSampleCount);
    }
    [Fact] public async Task Real_within_batch_dependencies_not_GUID_or_version_order_are_used() { await using var p = await PhotoFixture.Create(); await WithinBatch(p); }
    internal static async Task WithinBatch(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var capture = s.Capture(new("NG", null, null)) with { OperationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
        var b = s.Batch(capture); var hash = DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId, b, capture));
        var change = capture with { OperationId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Kind = "CorrectSample", Data = new("OK", null, null), Dependencies = [new(capture.OperationId, hash)] };
        var accepted = await s.Accept(b with { Operations = [change, capture] }); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var u = new BindingSetup(s, accepted.Operations.Single(x => x.OperationId == change.OperationId), photo, reference!);
        var r = (await Accept(u, Input(u))).Operation; Assert.Equal("NGToOKCorrection", r.Transition.Classification);
        Assert.Equal(capture.OperationId, r.Transition.Predecessor!.OperationId); Assert.Equal(r.Transition.Operation.DraftVersion, r.Transition.Predecessor.DraftVersion);
        Assert.Equal("UnsupportedUntilFutureWorkflow", (await DraftEvidenceAssessmentTests.Item(s)).EvidenceStatus);
    }
    [Fact] public async Task Strict_input_current_authority_and_bounded_reads() { await using var p = await PhotoFixture.Create(); await Authority(p); }
    internal static async Task Authority(PhotoFixture p) {
        var u = await Setup(p); var s = u.S; var b = Input(u); var json = JsonSerializer.Serialize(b, DraftCanonical.Json);
        foreach (var extra in new[] { "\"classification\":\"NGToOKCorrection\"", "\"provesCoverage\":true", "\"canExecute\":true" }) {
            var response = await s.Client().PostAsync(Url(s), new StringContent(json[..^1] + "," + extra + "}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var legacy = p.F.Client(p.F.Token(s.B, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await legacy.PostAsJsonAsync(Url(s), b)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Anonymous.GetAsync(ReadUrl(s))).StatusCode);
        var admin = await p.F.Login("admin"); Assert.Equal(HttpStatusCode.Forbidden, (await p.F.Client(admin.AccessToken).PostAsJsonAsync(Url(s), b)).StatusCode);
        var before = await Facts(p);
        foreach (var q in new[] { "?page=0", "?pageSize=101", "?pageSize=0", "?page=2147483647&pageSize=100", "?beforeDraftVersion=0" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(ReadUrl(s) + q)).StatusCode);
        Assert.Equal(before, await Facts(p)); await Accept(u, b);
        var old = s.B; (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(old.RefreshToken))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
    }
    [Fact] public async Task Binding_history_and_assessor_limits_are_explicit() { await using var p = await PhotoFixture.Create(); await Paging(p); }
    internal static async Task Paging(PhotoFixture p) {
        var u = await Setup(p);
        for (var n = 0; n < 101; n++) await Direct(u, Input(u, 2 + n));
        p.Fault.ReadCount = 0;
        var read = await Read(u, "?pageSize=100"); Assert.Equal(1, p.Fault.ReadCount);
        Assert.Equal(101, read.TotalBindings); Assert.Equal(100, read.Bindings.Count); Assert.Equal(100, read.History.Count); Assert.NotNull(read.HistoryBeforeDraftVersion);
        var older = await Read(u, "?page=2&pageSize=100&beforeDraftVersion=" + read.HistoryBeforeDraftVersion);
        Assert.Single(older.Bindings); Assert.Single(older.History); Assert.Null(older.HistoryBeforeDraftVersion);
        Assert.DoesNotContain(older.History[0].OperationId, read.History.Select(x => x.OperationId));
        var before = await Facts(p); p.Fault.ReadCount = 0;
        var i = await DraftEvidenceAssessmentTests.Item(u.S); Assert.Equal(1, p.Fault.ReadCount);
        Assert.False(i.BindingFactsComplete); Assert.Equal(100, i.AcceptedBindings!.Count); Assert.Equal(before, await Facts(p));
        // Observations never escape the read transaction/request or conceal later physical loss.
        File.Delete(await p.FinalPath(u.Photo.AttachmentId));
        Assert.All((await Read(u, "?pageSize=100")).Bindings, b => Assert.False(b.IsCurrentPhotoEligible));
        Assert.DoesNotContain((await DraftEvidenceAssessmentTests.Item(u.S)).Photos, photo => photo.IsEligibleEvidence);
    }
    [Fact] public async Task Unavailable_attempt_cannot_claim_complete_binding_observation() { await using var p = await PhotoFixture.Create(); await Unavailable(p); }
    internal static async Task Unavailable(PhotoFixture p) {
        var u = await Setup(p); await Accept(u, Input(u));
        await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == u.S.ItemId).ExecuteDeleteAsync());
        var before = await Facts(p); var i = await DraftEvidenceAssessmentTests.Item(u.S);
        Assert.Equal("Unavailable", i.Disposition); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
        Assert.False(i.SampleHistoryComplete); Assert.False(i.BindingFactsComplete); Assert.Empty(i.AcceptedBindings!);
        Assert.Single((await Read(u)).History); Assert.False(Assert.Single((await Read(u)).Bindings).IsCurrentAttempt); Assert.Equal(before, await Facts(p));
    }
}
