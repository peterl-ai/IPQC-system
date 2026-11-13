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

public sealed class DraftAttachmentTests
{
    internal static string Url(DraftSetup s) => s.Root + "/attachment-reference-batches";
    internal static DraftAttachmentBatchInput Input(DraftSetup s, AttachmentOutput a, long version = 0, WorkContextIssued? source = null,
        DraftAttachmentReceipt? previous = null, string kind = "AssociateAttachment") {
        var c = source ?? s.Source;
        var proofs = new[] { new DraftSourceProof(c.Context.Id, c.ProofSecret), new(s.Source.Context.Id, s.Source.ProofSecret) }.DistinctBy(x => x.WorkContextId).ToList();
        if (previous is not null) proofs = [new(c.Context.Id, c.ProofSecret)];
        return new(Guid.NewGuid(), 1, s.F.Line1, 2, s.F.Options.ValidEnvironment!, version,
            [new(Guid.NewGuid(), 1, kind, a.TaskItemId, a.InspectionAttemptId, a.AttachmentId, a.ClientPhotoId, a.Sha256!, c.Context.Id, c.Context.UserId,
                version, 1, previous is null ? null : new(previous.OperationId, previous.ContentHash))], proofs);
    }
    internal static async Task<(DraftSetup S, AttachmentOutput A, byte[] Bytes)> Setup(PhotoFixture p, bool ready = true) {
        var s = await SharedDraftFixture.Setup(p.F); var bytes = PhotoFixture.Image(); var a = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes));
        if (ready) a = await PhotoFixture.Ready(s, a.AttachmentId, bytes);
        else a = a with { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };
        return (s, a, bytes);
    }
    internal static async Task<DraftAttachmentBatchResult> Accept(DraftSetup s, DraftAttachmentBatchInput b, AuthOutput? actor = null) {
        var r = await s.Client(actor).PostAsJsonAsync(Url(s), b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString()); return (await r.Content.ReadFromJsonAsync<DraftAttachmentBatchResult>())!;
    }
    internal static async Task Counts(DraftSetup s, long version, int operations, int active, int batches) {
        Assert.Equal(version, (await s.Read())!.DraftVersion);
        Assert.Equal(operations, await s.F.Read(db => db.DraftAttachmentOperations.CountAsync()));
        Assert.Equal(active, await s.F.Read(db => db.DraftAttachmentProjections.CountAsync(x => x.IsAttached)));
        Assert.Equal(batches, await s.F.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(batches, await s.F.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await s.F.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskItemSamples.CountAsync()));
    }
    [Fact] public async Task Actual_bytes_provenance_detach_history_and_replay() { await using var p = await PhotoFixture.Create(); await Lifecycle(p); }
    internal static async Task Lifecycle(PhotoFixture p) {
        var (s, a, bytes) = await Setup(p); var freshSource = await WorkContextFixture.Issue(p.F, s.TaskId, s.B);
        var empty = (await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments"))!; Assert.Empty(empty.Associations); Assert.Empty(empty.History);
        await Counts(s, 0, 0, 0, 0); var frozen = await ItemRemovalTests.Retained(p.F);
        // The accepting actor differs from first storage B; copying the upload actor must fail this oracle.
        var b = Input(s, a, source: freshSource); var accepted = await Accept(s, b, s.A); var r = Assert.Single(accepted.Operations);
        Assert.Equal(s.A.User.Id, r.PhotoContributorUserId); Assert.Equal(s.B.User.Id, r.PhotoUploadedByUserId); Assert.Equal(s.B.PersonalSessionId, r.PhotoUploadPersonalSessionId);
        Assert.Equal(s.B.User.Id, r.ContributorUserId); Assert.Equal(s.A.User.Id, r.ActorUserId); Assert.Equal(s.A.PersonalSessionId, r.ActorPersonalSessionId);
        Assert.Equal(3, accepted.BatchReceipt!.ReceiptVersion); Assert.Empty(accepted.BatchReceipt.Operations); Assert.False(accepted.CanExecute); Assert.False(accepted.AcceptsFormalInspection);
        Assert.Equal(a.ClientPhotoId, r.ClientPhotoId); Assert.Equal(a.Sha256, r.Sha256); Assert.True(r.IsAttached); Assert.Equal(1, r.DraftVersion);
        var batchJson = await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.BatchId == b.BatchId).Select(x => x.ReceiptJson).SingleAsync());
        Assert.Equal(batchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt"));
        Assert.Equal(r, await s.Client().GetFromJsonAsync<DraftAttachmentReceipt>(s.Root + $"/operations/{r.OperationId}/receipt"));
        Assert.Equal(r, Assert.Single((await Accept(s, b with { SourceProofs = [] }, s.A)).Operations));
        var opReplay = await Accept(s, b with { BatchId = Guid.NewGuid(), ExpectedDraftVersion = 99, SourceProofs = [] }); Assert.True(opReplay.ReplayOnly); Assert.Null(opReplay.BatchReceipt); Assert.Equal(r, Assert.Single(opReplay.Operations));
        await SharedDraftFixture.Revoke(s); // storage Ready and existing accepted facts have separate security boundaries.
        var detach = Input(s, a, 1, freshSource, r, "DetachAttachment"); var detached = Assert.Single((await Accept(s, detach)).Operations);
        Assert.False(detached.IsAttached); Assert.Equal(r.PhotoContributorUserId, detached.PhotoContributorUserId); Assert.Equal(r.PhotoUploadedByUserId, detached.PhotoUploadedByUserId);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(await p.FinalPath(a.AttachmentId))); Assert.Equal(frozen, await ItemRemovalTests.Retained(p.F));
        Assert.Equal(batchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt"));
        var history = (await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments"))!;
        Assert.False(Assert.Single(history.Associations).IsAttached); Assert.Equal(2, history.History.Count); Assert.Contains(r, history.History); Assert.False(history.CanExecute);
        // Reattach the same already accepted immutable photo frontier under B's fresh operation.
        await Accept(s, Input(s, a, 2, freshSource, detached)); await Counts(s, 3, 3, 1, 3);
        var stored = await p.F.Read(async db => JsonSerializer.Serialize(await db.DraftAttachmentOperations.AsNoTracking().ToListAsync()));
        foreach (var secret in new[] { s.Source.ProofSecret, freshSource.ProofSecret, s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken! }) Assert.DoesNotContain(secret, stored + batchJson);
        Assert.DoesNotContain("StorageKey", batchJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(p.DirectoryPath, batchJson);
        Assert.Equal(1, await p.F.Read(db => db.AttachmentAcceptances.CountAsync()));
    }
    [Theory][InlineData("pending")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")]
    public async Task Physical_readiness_is_not_a_trusted_flag(string kind) { await using var p = await PhotoFixture.Create(); await Readiness(p, kind); }
    internal static async Task Readiness(PhotoFixture p, string kind) {
        var (s, a, _) = await Setup(p, kind != "pending");
        if (kind == "missing") File.Delete(await p.FinalPath(a.AttachmentId));
        if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(a.AttachmentId), [1, 2, 3]);
        if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(u => u.SetProperty(x => x.State, "MissingUnexpected")));
        var r = await s.Client().PostAsJsonAsync(Url(s), Input(s, a)); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("AttachmentNotReady", await r.Content.ReadAsStringAsync()); await Counts(s, 0, 0, 0, 0);
    }
    [Theory][InlineData("item")][InlineData("attempt")][InlineData("client")][InlineData("hash")][InlineData("line")][InlineData("environment")][InlineData("workflow")][InlineData("removed")][InlineData("stale")][InlineData("task")]
    public async Task Exact_current_scope(string kind) { await using var p = await PhotoFixture.Create(); await Scope(p, kind); }
    internal static async Task Scope(PhotoFixture p, string kind) {
        var (s, a, _) = await Setup(p); var b = Input(s, a); var o = b.Operations[0];
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        b = kind switch {
            "item" => b with { Operations = [o with { TaskItemId = other.TaskItemId }] },
            "attempt" => b with { Operations = [o with { InspectionAttemptId = other.InspectionAttemptId }] },
            "client" => b with { Operations = [o with { ClientPhotoId = Guid.NewGuid() }] }, "hash" => b with { Operations = [o with { Sha256 = new string('A', 64) }] },
            "line" => b with { ExpectedTaskLineId = p.F.Line2 }, "environment" => b with { ExpectedEnvironmentId = "other" },
            "workflow" => b with { ExpectedWorkflowVersion = 1 }, _ => b };
        if (kind == "removed") await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s));
        if (kind == "stale") await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync()); // technical lifecycle fence, not a fake producer.
        if (kind == "task") {
            p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(p.F));
            var actual = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync());
            s = s with { TaskId = actual, B = await p.F.Login("dev-ipqa-2") }; Assert.NotEmpty((await s.Read())!.CurrentAttempts);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        Assert.Equal(0, await p.F.Read(db => db.DraftAttachmentOperations.CountAsync()));
    }
    [Theory][InlineData("explicit")][InlineData("reset")][InlineData("role")][InlineData("deactivate")]
    public async Task Ready_before_security_is_not_business_acceptance(string action) { await using var p = await PhotoFixture.Create(); await Revoked(p, action); }
    internal static async Task Revoked(PhotoFixture p, string action) {
        var (s, a, _) = await Setup(p); var bSource = await WorkContextFixture.Issue(p.F, s.TaskId, s.B); var b = Input(s, a, source: bSource);
        await SharedDraftFixture.Revoke(s, action); var r = await s.Client().PostAsJsonAsync(Url(s), b); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement; Assert.Equal("SourceSecurityRevoked", json.GetProperty("code").GetString());
        Assert.True(json.GetProperty("quarantineRequired").GetBoolean()); Assert.Equal(b.Operations[0].OperationId, json.GetProperty("operationIds")[0].GetGuid());
        Assert.Equal(s.Source.Context.Id, json.GetProperty("sourceContextIds")[0].GetGuid()); await Counts(s, 0, 0, 0, 0);
        Assert.True(File.Exists(await p.FinalPath(a.AttachmentId))); Assert.Equal(1, await p.F.Read(db => db.AttachmentAcceptances.CountAsync()));
    }
    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("self-password")][InlineData("refresh")]
    public async Task Normal_source_lifecycle(string action) { await using var p = await PhotoFixture.Create(); await Normal(p, action); }
    internal static async Task Normal(PhotoFixture p, string action) {
        var (s, a, _) = await Setup(p);
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        else if (action == "refresh") _ = await p.F.Refresh(s.A.RefreshToken!);
        else p.F.Clock.Now = p.F.Clock.Now.AddDays(15);
        s = s with { B = await p.F.Login("dev-ipqa-2") }; var receipt = Assert.Single((await Accept(s, Input(s, a))).Operations);
        Assert.Equal(s.A.User.Id, receipt.PhotoContributorUserId); Assert.Null(await p.F.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
    }
    [Fact] public async Task Authority_strict_shape_and_atomic_mixed_rejection() { await using var p = await PhotoFixture.Create(); await Authority(p); }
    internal static async Task Authority(PhotoFixture p) {
        var (s, a, _) = await Setup(p); var b = Input(s, a);
        var anonymous = await p.F.Anonymous.PostAsync(Url(s), new StringContent("{bad", Encoding.UTF8, "application/json")); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var admin = await p.F.Login("admin"); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(admin).PostAsJsonAsync(Url(s), b)).StatusCode);
        var noFamily = p.F.Token(s.B, cs => cs.Where(c => c.Type != "personal_session_id").ToList()); Assert.Equal(HttpStatusCode.Unauthorized, (await p.F.Client(noFamily).PostAsJsonAsync(Url(s), b)).StatusCode);
        var json = JsonSerializer.Serialize(b, DraftCanonical.Json);
        foreach (var bad in new[] { json[..^1] + ",\"accepted\":true}", json.Replace("\"expectedDraftVersion\":0", "\"expectedDraftVersion\":\"0\""), json[..^1] + ",\"batchId\":\"" + b.BatchId + "\"}" }) {
            Assert.NotEqual(json, bad); Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().PostAsync(Url(s), new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await s.Client().PostAsync(Url(s), new StringContent(new string('x', 131073), Encoding.UTF8, "application/json"))).StatusCode);
        foreach (var invalid in new[] { b with { Operations = [] }, b with { Operations = Enumerable.Repeat(b.Operations[0], 11).ToList() },
            b with { Operations = [b.Operations[0], b.Operations[0] with { OperationId = Guid.NewGuid() }] }, b with { SourceProofs = [] },
            b with { Operations = [b.Operations[0] with { ContributorUserIdAssertion = s.B.User.Id }] },
            b with { Operations = [b.Operations[0], b.Operations[0] with { OperationId = Guid.NewGuid(), AttachmentId = Guid.NewGuid() }] } }) {
            var r = await s.Client().PostAsJsonAsync(Url(s), invalid); Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        }
        await using var scope = p.F.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        var ex = await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptAttachmentsAsync(s.TaskId, b, new ClaimsPrincipal(new ClaimsIdentity()), default)); Assert.Equal(401, ex.Status);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        s = s with { B = await p.F.Login("dev-ipqa-2") }; await Counts(s, 0, 0, 0, 0);
    }
    [Fact] public async Task Exact_replay_changed_identity_and_predecessor_conflicts() { await using var p = await PhotoFixture.Create(); await Conflicts(p); }
    internal static async Task Conflicts(PhotoFixture p) {
        var (s, a, _) = await Setup(p); var b = Input(s, a); var r = Assert.Single((await Accept(s, b)).Operations); var o = b.Operations[0];
        foreach (var changed in new[] { b with { CanonicalizationVersion = 2 }, b with { BatchId = Guid.NewGuid(), Operations = [o with { CanonicalizationVersion = 2 }] },
            b with { Operations = [o with { OriginLocalEditVersion = 2 }] }, b with { BatchId = Guid.NewGuid(), Operations = [o with { SourceWorkContextId = Guid.NewGuid() }] },
            b with { BatchId = Guid.NewGuid(), Operations = [o with { Kind = "DetachAttachment" }] },
            Input(s, a, 1), Input(s, a, 1, previous: r, kind: "DetachAttachment") with { Operations = [o with { OperationId = Guid.NewGuid(), Kind = "DetachAttachment", BaseDraftVersion = 1, Predecessor = new(r.OperationId, new string('A', 64)) }] } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), changed)).StatusCode);
        await Counts(s, 1, 1, 1, 1);
    }
    [Fact] public async Task Five_ready_photos_one_atomic_batch_and_no_sample_facts() { await using var p = await PhotoFixture.Create(); await Limits(p); }
    internal static async Task Limits(PhotoFixture p) {
        var (s, first, bytes) = await Setup(p); var photos = new List<AttachmentOutput> { first };
        for (var i = 0; i < 4; i++) { var a = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); photos.Add(await PhotoFixture.Ready(s, a.AttachmentId, bytes)); }
        var batch = Input(s, first) with { Operations = photos.Select(a => Input(s, a).Operations[0]).ToList() }; var accepted = await Accept(s, batch);
        Assert.Equal(5, accepted.Operations.Count); await Counts(s, 1, 5, 5, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(PhotoFixture.Root(s), PhotoFixture.Input(s, bytes))).StatusCode);
        Assert.Equal(0, await p.F.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.ContributionOperations.CountAsync()));
        Assert.All((await s.Read())!.Items!, x => Assert.Equal(1, x.MinimumSampleCount)); Assert.False((await s.Read())!.CanExecute);
    }
    [Fact] public async Task Accepted_A_frontier_and_new_B_work_resolve_before_quarantine() { await using var p = await PhotoFixture.Create(); await Mixed(p); }
    internal static async Task Mixed(PhotoFixture p) {
        var (s, a, bytes) = await Setup(p); var firstBatch = Input(s, a); var original = Assert.Single((await Accept(s, firstBatch)).Operations);
        var sourceB = await WorkContextFixture.Issue(p.F, s.TaskId, s.B); var sb = s with { Source = sourceB };
        var second = await PhotoFixture.Reserve(sb, PhotoFixture.Input(sb, bytes)); second = await PhotoFixture.Ready(sb, second.AttachmentId, bytes);
        var unacceptedA = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); unacceptedA = await PhotoFixture.Ready(s, unacceptedA.AttachmentId, bytes);
        var json = await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.BatchId == firstBatch.BatchId).Select(x => x.ReceiptJson).SingleAsync());
        await SharedDraftFixture.Revoke(s);
        var freshB = Input(s, second, 1, sourceB); var mixed = freshB with { Operations = [firstBatch.Operations[0], freshB.Operations[0]], SourceProofs = [new(sourceB.Context.Id, sourceB.ProofSecret)] };
        var accepted = await Accept(s, mixed); Assert.Equal(2, accepted.Operations.Count); Assert.Contains(original, accepted.Operations);
        Assert.Equal(json, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{firstBatch.BatchId}/receipt"));
        var currentB = Assert.Single(accepted.Operations, x => x.AttachmentId == second.AttachmentId);
        var detachB = Input(s, second, 2, sourceB, currentB, "DetachAttachment"); var freshA = Input(s, unacceptedA, 2, sourceB);
        var bad = detachB with { Operations = [detachB.Operations[0], freshA.Operations[0]], SourceProofs = freshA.SourceProofs };
        var rejected = await s.Client().PostAsJsonAsync(Url(s), bad); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("SourceSecurityRevoked", problem.GetProperty("code").GetString()); Assert.Equal(freshA.Operations[0].OperationId, Assert.Single(problem.GetProperty("operationIds").EnumerateArray()).GetGuid());
        await Counts(s, 2, 2, 2, 2); Assert.True(await p.F.Read(db => db.DraftAttachmentProjections.Where(x => x.AttachmentId == second.AttachmentId).Select(x => x.IsAttached).SingleAsync()));
        Assert.False(await p.F.Read(db => db.DraftAttachmentOperations.AnyAsync(x => x.OperationId == detachB.Operations[0].OperationId)));
        Assert.Equal(3, await p.F.Read(db => db.AttachmentAcceptances.CountAsync()));
    }
}
