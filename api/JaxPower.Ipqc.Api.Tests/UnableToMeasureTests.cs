using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed record UnableSetup(DraftSetup S, AttachmentOutput Photo, DraftAttachmentReceipt Reference);
public sealed class UnableToMeasureTests
{
    internal static string Url(DraftSetup s) => s.Root + "/item-disposition-batches";
    internal static async Task<UnableSetup> Setup(PhotoFixture p, bool sampled = false) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p);
        if (sampled) await s.Accept(s.Batch(s.Capture(new("NG", null, null))));
        var reference = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, sampled ? 1 : 0))).Operations);
        return new(s, a, reference);
    }
    internal static UnableToMeasureBatchInput Input(UnableSetup s, long? version = null, WorkContextIssued? source = null,
        string kind = "SetUnableToMeasureNG", UnableToMeasureReceipt? previous = null, string? reason = "Probe cannot reach inspection surface") {
        var c = source ?? s.S.Source; var v = version ?? s.Reference.DraftVersion; var clear = kind == "ClearUnableToMeasureNG";
        return new(Guid.NewGuid(), 1, s.S.F.Line1, 2, s.S.F.Options.ValidEnvironment!, v,
            new(Guid.NewGuid(), 1, kind, s.S.ItemId, s.S.AttemptId, c.Context.Id, c.Context.UserId, v, 1,
                clear ? null : reason, clear ? null : new(s.Reference.OperationId, s.Reference.ContentHash),
                previous is null ? null : new(previous.OperationId, previous.ContentHash)), new(c.Context.Id, c.ProofSecret));
    }
    internal static async Task<UnableToMeasureBatchResult> Accept(UnableSetup u, UnableToMeasureBatchInput b, AuthOutput? actor = null) {
        var r = await u.S.Client(actor).PostAsJsonAsync(Url(u.S), b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString()); return (await r.Content.ReadFromJsonAsync<UnableToMeasureBatchResult>())!;
    }
    internal static async Task<DraftDispositionOutput> Read(UnableSetup u) => (await u.S.Client().GetFromJsonAsync<DraftDispositionOutput>(u.S.Root + "/shared-draft/item-dispositions"))!;
    internal static async Task Counts(UnableSetup u, long version, int ops, bool active) {
        var f = u.S.F; var draft = (await u.S.Read())!; Assert.Equal(version, draft.DraftVersion); Assert.False(draft.CanExecute);
        Assert.Equal(ops, await f.Read(db => db.UnableToMeasureOperations.CountAsync()));
        Assert.Equal(active ? 1 : 0, await f.Read(db => db.UnableToMeasureProjections.CountAsync(x => x.IsActive)));
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.PatrolTaskItemSamples.CountAsync()));
        await f.Read(async db => { var t = await db.PatrolTasks.SingleAsync(x => x.Id == u.S.TaskId); Assert.Null(t.StartedAtUtc); Assert.Equal(0, t.CurrentRevisionNo); return true; });
    }
    [Fact] public async Task Real_evidence_reason_history_correction_clear_and_replay() { await using var p = await PhotoFixture.Create(); await Lifecycle(p); }
    internal static async Task Lifecycle(PhotoFixture p) {
        var u = await Setup(p, true); var s = u.S; var f = p.F; var before = await ItemRemovalTests.Retained(f);
        var bSource = await WorkContextFixture.Issue(f, s.TaskId, s.B); var b = Input(u, source: bSource);
        var result = await Accept(u, b, s.A); var r = result.Operation;
        Assert.Equal("UnableToMeasureNG", r.Disposition); Assert.Equal("NG", r.Judgment); Assert.Equal(b.Operation.NgReason, r.NgReason);
        Assert.Equal(s.B.User.Id, r.ContributorUserId); Assert.Equal(s.A.User.Id, r.ActorUserId); Assert.Equal(s.A.PersonalSessionId, r.ActorPersonalSessionId);
        Assert.Equal(s.A.User.Id, r.Evidence!.PhotoContributorUserId); Assert.Equal(s.B.User.Id, r.Evidence.PhotoUploadedByUserId); Assert.Equal(u.Reference, r.Evidence);
        Assert.Equal(1, r.MinimumSampleCount); Assert.False(result.CanExecute); Assert.False(result.AcceptsFormalInspection); Assert.Equal(4, result.BatchReceipt!.ReceiptVersion);
        Assert.Equal(before, await ItemRemovalTests.Retained(f)); await Counts(u, 3, 1, true);
        var read = await Read(u); var item = read.Items.Single(x => x.TaskItemId == s.ItemId); Assert.Equal("Satisfied", item.DraftEvidenceStatus); Assert.True(item.Evidence!.IsValid);
        Assert.Equal("UnableToMeasureNG", (await s.Read())!.Items!.Single(x => x.TaskItemId == s.ItemId).Disposition); Assert.False(Assert.Single((await s.Read())!.Samples).IsCurrentApplicable);
        var batchJson = await f.Read(db => db.AcceptedBatchReceipts.Where(x => x.BatchId == b.BatchId).Select(x => x.ReceiptJson).SingleAsync());
        f.Clock.Now = f.Clock.Now.AddMinutes(1); // Recomputing a replay timestamp is now observably wrong.
        var replay = await Accept(u, b with { SourceProof = null }); Assert.True(replay.ReplayOnly); Assert.Equal(r, replay.Operation);
        var opReplay = await Accept(u, b with { BatchId = Guid.NewGuid(), ExpectedDraftVersion = 99, SourceProof = null }); Assert.True(opReplay.ReplayOnly); Assert.Null(opReplay.BatchReceipt); Assert.Equal(r, opReplay.Operation);
        Assert.Equal(batchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt"));
        Assert.Equal(r, await s.Client().GetFromJsonAsync<UnableToMeasureReceipt>(s.Root + $"/operations/{r.OperationId}/receipt"));
        var correction = Input(u, 3, bSource, "CorrectUnableToMeasureNG", r, "Guard prevents measurement; photo documents obstruction");
        var corrected = (await Accept(u, correction)).Operation; Assert.Equal("UnableToMeasureNG", corrected.Disposition); Assert.Equal(r.OperationId, corrected.Predecessor!.OperationId);
        await SharedDraftFixture.Revoke(s); // Accepted photo/history survives A revocation; B's new source is independently eligible.
        Assert.Equal(r, (await Accept(u, b with { SourceProof = null })).Operation);
        var clear = (await Accept(u, Input(u, 4, bSource, "ClearUnableToMeasureNG", corrected))).Operation;
        Assert.Equal("Applicable", clear.Disposition); Assert.Null(clear.Judgment); Assert.Null(clear.NgReason); Assert.Null(clear.Evidence);
        Assert.Equal(before, await ItemRemovalTests.Retained(f)); await Counts(u, 5, 3, false);
        Assert.True(Assert.Single((await s.Read())!.Samples).IsCurrentApplicable); Assert.Equal("Applicable", (await Read(u)).Items.Single(x => x.TaskItemId == s.ItemId).Disposition);
        var again = (await Accept(u, Input(u, 5, bSource, previous: clear))).Operation; Assert.Equal("UnableToMeasureNG", again.Disposition);
        await Counts(u, 6, 4, true); Assert.Equal(batchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt"));
        var business = await f.Read(async db => JsonSerializer.Serialize(await db.UnableToMeasureOperations.AsNoTracking().ToListAsync()));
        foreach (var secret in new[] { s.Source.ProofSecret, bSource.ProofSecret, DraftCanonical.Hash(s.Source.ProofSecret), s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken! }) Assert.DoesNotContain(secret, business + batchJson);
        Assert.DoesNotContain("StorageKey", batchJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(p.DirectoryPath, batchJson);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("   ")][InlineData("\t")][InlineData("control\u0001")]
    public async Task Reason_is_mandatory_bounded_text(string? reason) { await using var p = await PhotoFixture.Create(); await Reason(p, reason); }
    internal static async Task Reason(PhotoFixture p, string? reason) {
        var u = await Setup(p); var r = await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, reason: reason));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); await Counts(u, 1, 0, false);
    }
    [Fact] public async Task Reason_limit_and_unaccepted_identity_conflicts() { await using var p = await PhotoFixture.Create(); await Conflicts(p); }
    internal static async Task Conflicts(PhotoFixture p) {
        var u = await Setup(p); var b = Input(u, reason: new string('x', 2001));
        Assert.Equal(HttpStatusCode.BadRequest, (await u.S.Client().PostAsJsonAsync(Url(u.S), b)).StatusCode);
        b = Input(u, reason: new string('x', 2000)); var first = (await Accept(u, b)).Operation;
        var otherSource = await WorkContextFixture.Issue(p.F, u.S.TaskId, u.S.B);
        foreach (var changed in new[] { b with { Operation = b.Operation with { NgReason = "changed" } },
            b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { SourceWorkContextId = otherSource.Context.Id, ContributorUserIdAssertion = otherSource.Context.UserId }, SourceProof = new(otherSource.Context.Id, otherSource.ProofSecret) },
            b with { CanonicalizationVersion = 2 }, b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { CanonicalizationVersion = 2 } },
            Input(u, 2), Input(u, 2, kind: "CorrectUnableToMeasureNG"),
            Input(u, 2, kind: "CorrectUnableToMeasureNG", previous: first) with { Operation = b.Operation with { OperationId = Guid.NewGuid(), Kind = "CorrectUnableToMeasureNG", BaseDraftVersion = 2, Predecessor = new(first.OperationId, new string('A', 64)) } } })
            Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), changed)).StatusCode);
        await Counts(u, 2, 1, true);
    }
    [Theory][InlineData("unassociated")][InlineData("pending")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")][InlineData("detached")][InlineData("superseded")]
    public async Task Evidence_is_genuine_active_and_available(string kind) { await using var p = await PhotoFixture.Create(); await Evidence(p, kind); }
    internal static async Task Evidence(PhotoFixture p, string kind) {
        UnableSetup u; var version = 1L;
        if (kind is "unassociated" or "pending") {
            var (s, a, _) = await DraftAttachmentTests.Setup(p, kind != "pending");
            u = new(s, a, new(s.TaskId, Guid.NewGuid(), Guid.NewGuid(), 1, new string('A', 64), "AssociateAttachment", s.ItemId, s.AttemptId, a.AttachmentId,
                a.ClientPhotoId, a.Sha256!, s.Source.Context.Id, s.A.User.Id, s.B.User.Id, s.B.PersonalSessionId!.Value, s.Source.Context.Id, s.A.User.Id, s.B.User.Id,
                s.B.PersonalSessionId.Value, s.F.Options.ValidEnvironment!, s.F.Line1, 2, s.F.Clock.Now.UtcDateTime, 1, null, true));
            version = 0; // Deliberately forged CLIENT claim only; no fake server ledger or Ready row.
        } else u = await Setup(p);
        if (kind == "missing") File.Delete(await p.FinalPath(u.Photo.AttachmentId));
        if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(u.Photo.AttachmentId), [1, 2, 3]);
        if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(v => v.SetProperty(x => x.State, "MissingUnexpected")));
        if (kind is "detached" or "superseded") {
            var detach = Assert.Single((await DraftAttachmentTests.Accept(u.S, DraftAttachmentTests.Input(u.S, u.Photo, 1, previous: u.Reference, kind: "DetachAttachment"))).Operations); version = 2;
            if (kind == "superseded") { await DraftAttachmentTests.Accept(u.S, DraftAttachmentTests.Input(u.S, u.Photo, 2, previous: detach)); version = 3; }
        }
        Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, version))).StatusCode); await Counts(u, version, 0, false);
    }
    [Theory][InlineData("item")][InlineData("attempt")][InlineData("task")][InlineData("line")][InlineData("environment")][InlineData("workflow")][InlineData("hash")][InlineData("stale")][InlineData("removed")]
    public async Task Exact_task_item_attempt_and_scope(string kind) { await using var p = await PhotoFixture.Create(); await Scope(p, kind); }
    internal static async Task Scope(PhotoFixture p, string kind) {
        var u = await Setup(p); var b = Input(u); var s = u.S; var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        b = kind switch {
            "item" => b with { Operation = b.Operation with { TaskItemId = other.TaskItemId, InspectionAttemptId = other.InspectionAttemptId } },
            "attempt" => b with { Operation = b.Operation with { InspectionAttemptId = other.InspectionAttemptId } },
            "line" => b with { ExpectedTaskLineId = p.F.Line2 }, "environment" => b with { ExpectedEnvironmentId = "other" }, "workflow" => b with { ExpectedWorkflowVersion = 1 },
            "hash" => b with { Operation = b.Operation with { Evidence = new(u.Reference.OperationId, new string('A', 64)) } }, _ => b };
        if (kind == "stale") await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync());
        if (kind == "removed") await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1));
        if (kind == "task") {
            p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(p.F));
            var task = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync()); s = s with { TaskId = task, B = await p.F.Login("dev-ipqa-2") };
            Assert.NotEmpty((await s.Read())!.CurrentAttempts);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode); Assert.Equal(0, await p.F.Read(db => db.UnableToMeasureOperations.CountAsync()));
    }
    [Theory][InlineData("detach")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")]
    public async Task Later_evidence_loss_is_truthfully_incomplete(string kind) { await using var p = await PhotoFixture.Create(); await LaterLoss(p, kind); }
    internal static async Task LaterLoss(PhotoFixture p, string kind) {
        var u = await Setup(p); var b = Input(u); var r = (await Accept(u, b)).Operation; var json = await p.F.Read(db => db.UnableToMeasureOperations.Select(x => x.ReceiptJson).SingleAsync());
        if (kind == "detach") await DraftAttachmentTests.Accept(u.S, DraftAttachmentTests.Input(u.S, u.Photo, 2, previous: u.Reference, kind: "DetachAttachment"));
        if (kind == "missing") File.Delete(await p.FinalPath(u.Photo.AttachmentId));
        if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(u.Photo.AttachmentId), [1]);
        if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(v => v.SetProperty(x => x.State, "Corrupt")));
        var item = (await Read(u)).Items.Single(x => x.TaskItemId == u.S.ItemId); Assert.Equal("UnableToMeasureNG", item.Disposition); Assert.Equal("Incomplete", item.DraftEvidenceStatus); Assert.False(item.Evidence!.IsValid);
        Assert.Equal(json, await p.F.Read(db => db.UnableToMeasureOperations.Select(x => x.ReceiptJson).SingleAsync())); Assert.Equal(r, (await Accept(u, b with { SourceProof = null })).Operation);
    }
    [Fact] public async Task Active_disposition_blocks_fresh_samples_and_Removed_but_not_history() { await using var p = await PhotoFixture.Create(); await Writes(p); }
    internal static async Task Writes(PhotoFixture p) {
        var u = await Setup(p, true); var s = u.S; var capture = await p.F.Read(db => db.ContributionOperations.AsNoTracking().SingleAsync());
        var receipt = JsonSerializer.Deserialize<DraftOperationReceipt>(capture.ReceiptJson, DraftCanonical.Json)!; var r = (await Accept(u, Input(u))).Operation;
        var before = await ItemRemovalTests.Retained(p.F);
        var captureNew = s.Batch(s.Capture(version: 3), 3); var correct = s.Mutation(receipt, "CorrectSample", new("OK", null, null)); var remove = s.Mutation(receipt, "RemoveSample", null);
        foreach (var b in new[] { captureNew, correct, remove }) {
            var current = b with { ExpectedDraftVersion = 3, Operations = b.Operations.Select(o => o with { BaseDraftVersion = 3 }).ToList() };
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", current)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(s, 3))).StatusCode);
        Assert.Equal(before, await ItemRemovalTests.Retained(p.F)); await Counts(u, 3, 1, true);
        await Accept(u, Input(u, 3, kind: "ClearUnableToMeasureNG", previous: r));
        await s.Accept(s.Batch(s.Capture(version: 4), 4)); Assert.Equal(2, await p.F.Read(db => db.SharedSamples.CountAsync()));
    }
    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("self-password")][InlineData("refresh")]
    public async Task Ordinary_source_lifecycle_is_not_security_revoke(string action) { await using var p = await PhotoFixture.Create(); await Normal(p, action); }
    internal static async Task Normal(PhotoFixture p, string action) {
        var u = await Setup(p); var s = u.S;
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        else if (action == "refresh") _ = await p.F.Refresh(s.A.RefreshToken!); else p.F.Clock.Now = p.F.Clock.Now.AddDays(15);
        u = u with { S = s with { B = await p.F.Login("dev-ipqa-2") } }; Assert.Equal(s.A.User.Id, (await Accept(u, Input(u))).Operation.ContributorUserId);
        Assert.Null(await p.F.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
    }
    [Fact] public async Task Authority_shape_body_and_proof_are_independent() { await using var p = await PhotoFixture.Create(); await Authority(p); }
    internal static async Task Authority(PhotoFixture p) {
        var u = await Setup(p); var s = u.S; var b = Input(u); var json = JsonSerializer.Serialize(b, DraftCanonical.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Anonymous.PostAsync(Url(s), new StringContent("{bad", Encoding.UTF8, "application/json"))).StatusCode);
        var admin = await p.F.Login("admin"); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(admin).PostAsJsonAsync(Url(s), b)).StatusCode);
        var noFamily = p.F.Token(s.B, cs => cs.Where(c => c.Type != "personal_session_id").ToList()); Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Client(noFamily).PostAsJsonAsync(Url(s), b)).StatusCode);
        foreach (var bad in new[] { json[..^1] + ",\"accepted\":true}", json.Replace("\"expectedDraftVersion\":1", "\"expectedDraftVersion\":\"1\""), json[..^1] + ",\"batchId\":\"" + b.BatchId + "\"}" }) {
            Assert.NotEqual(json, bad); Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().PostAsync(Url(s), new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await s.Client().PostAsync(Url(s), new StringContent(new string('x', 131073), Encoding.UTF8, "application/json"))).StatusCode);
        foreach (var bad in new[] { b with { SourceProof = null }, b with { Operation = b.Operation with { ContributorUserIdAssertion = s.B.User.Id } }, b with { Operation = b.Operation with { Kind = "N/A" } } })
            Assert.Contains((await s.Client().PostAsJsonAsync(Url(s), bad)).StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
        await using var scope = p.F.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        var e = await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptUnableAsync(s.TaskId, b, new ClaimsPrincipal(new ClaimsIdentity()), default)); Assert.Equal(401, e.Status);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
    }
}
