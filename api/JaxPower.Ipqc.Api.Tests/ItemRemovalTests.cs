using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ItemRemovalTests
{
    internal static string Url(DraftSetup s) => s.Root + "/item-removal-batches";
    internal static ItemRemovalBatchInput Input(DraftSetup s, long version = 0, DraftAttemptScope? target = null, string? reason = null) =>
        new(Guid.NewGuid(), 1, s.F.Line1, 2, PersonalSessionFixture.Environment, version,
            new(Guid.NewGuid(), 1, "RemoveItem", target?.TaskItemId ?? s.ItemId, target?.InspectionAttemptId ?? s.AttemptId,
                s.Source.Context.Id, s.Source.Context.UserId, version, 1, true, "Production", reason),
            new(s.Source.Context.Id, s.Source.ProofSecret));
    internal static async Task<ItemRemovalBatchResult> Accept(DraftSetup s, ItemRemovalBatchInput b, AuthOutput? user = null) {
        var response = await s.Client(user).PostAsJsonAsync(Url(s), b);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<ItemRemovalBatchResult>())!;
    }
    internal static Task<string> Retained(PersonalSessionFixture f) => f.Read(async db => JsonSerializer.Serialize(new {
        Items = await db.PatrolTaskItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Attempts = await db.InspectionAttempts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Applicabilities = await db.InspectionAttemptApplicabilities.AsNoTracking().OrderBy(x => x.PatrolTaskItemId).ToListAsync(),
        Samples = await db.SharedSamples.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Projections = await db.SharedSampleProjections.AsNoTracking().OrderBy(x => x.SampleId).ToListAsync(),
        Operations = await db.ContributionOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(),
        Dependencies = await db.ContributionDependencies.AsNoTracking().OrderBy(x => x.OperationId).ThenBy(x => x.DependencyId).ToListAsync()
    }));
    internal static async Task Counts(DraftSetup s, long version, int removals, int samples, int sampleOperations, int batches) {
        Assert.Equal(version, (await s.Read())!.DraftVersion);
        Assert.Equal(removals, await s.F.Read(db => db.ItemRemovalOperations.CountAsync()));
        Assert.Equal(samples, await s.F.Read(db => db.SharedSamples.CountAsync()));
        Assert.Equal(sampleOperations, await s.F.Read(db => db.ContributionOperations.CountAsync()));
        Assert.Equal(removals + sampleOperations, await s.F.Read(db => db.SharedOperationIdentities.CountAsync()));
        Assert.Equal(batches, await s.F.Read(db => db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(batches, await s.F.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskSubmissions.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskItemSamples.CountAsync()));
    }

    [Fact] public async Task RM01_05_06_09_11_12_Real_item_removal_preserves_history_and_receipts()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Lifecycle(f); }
    internal static async Task Lifecycle(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f, true);
        var captureBatch = s.Batch(s.Capture(new(null, 2.6m, "retained barcode")));
        var capture = Assert.Single((await s.Accept(captureBatch)).Operations);
        var history = await Retained(f);
        var oldBatchJson = await f.Read(db => db.AcceptedBatchReceipts.Where(x => x.BatchId == captureBatch.BatchId).Select(x => x.ReceiptJson).SingleAsync());
        var b = Input(s, 1, reason: "Production confirms item is absent");
        var result = await Accept(s, b); var receipt = result.Operation;
        Assert.Equal("RemoveItem", receipt.Kind); Assert.Equal("Removed", receipt.Disposition);
        Assert.Equal(TestAccounts.Ipqa1Id, receipt.ContributorUserId); Assert.Equal(TestAccounts.Ipqa2Id, receipt.UploadedByUserId);
        Assert.Equal(s.B.PersonalSessionId, receipt.UploadedPersonalSessionId); Assert.Equal(2, receipt.DraftVersion);
        Assert.Equal(1, receipt.MinimumSampleCount); Assert.Equal(1, receipt.RemainingApplicableItemCount);
        Assert.False(receipt.WarnsLastApplicableItemRemoval); Assert.False(result.CanExecute); Assert.False(result.AcceptsFormalInspection);
        Assert.Empty(result.BatchReceipt!.Operations); Assert.Equal(2, result.BatchReceipt.ReceiptVersion);
        Assert.Equal(receipt, Assert.Single(result.BatchReceipt.ItemOperations!));
        var output = (await s.Read())!; Assert.False(output.AllItemsRemoved); Assert.False(output.CanExecute);
        Assert.Equal("Removed", output.Items!.Single(x => x.TaskItemId == s.ItemId).Disposition);
        var sample = Assert.Single(output.Samples); Assert.False(sample.IsCurrentApplicable); Assert.False(sample.IsRemoved);
        Assert.Equal("NG", sample.Value.Judgment); Assert.Equal(2.6m, sample.Value.InspectionValue);
        Assert.Equal(history, await Retained(f));
        Assert.Equal(oldBatchJson, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{captureBatch.BatchId}/receipt"));
        Assert.Equal(capture, await s.Client().GetFromJsonAsync<DraftOperationReceipt>(s.Root + $"/operations/{capture.OperationId}/receipt"));
        var replay = await Accept(s, b with { SourceProof = null }, s.A);
        Assert.True(replay.ReplayOnly); Assert.Equal(receipt, replay.Operation);
        var opReplay = await Accept(s, b with { BatchId = Guid.NewGuid(), ExpectedDraftVersion = 99, SourceProof = null });
        Assert.True(opReplay.ReplayOnly); Assert.Null(opReplay.BatchReceipt); Assert.Equal(receipt, opReplay.Operation);
        foreach (var changed in new[] { b with { Operation = b.Operation with { Reason = "changed" } },
            b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { ConfirmationSource = "Equipment" } },
            b with { CanonicalizationVersion = 2 }, b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { CanonicalizationVersion = 2 } } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), changed)).StatusCode);
        var bSource = await WorkContextFixture.Issue(f, s.TaskId, s.B);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), b with { BatchId = Guid.NewGuid(),
            Operation = b.Operation with { SourceWorkContextId = bSource.Context.Id, ContributorUserIdAssertion = bSource.Context.UserId },
            SourceProof = new(bSource.Context.Id, bSource.ProofSecret) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(s, 2))).StatusCode);
        var captureAgain = s.Capture(new(null, 2m, null), version: 2);
        var correction = s.Mutation(capture, "CorrectSample", new(null, 2m, null));
        var sampleRemoval = s.Mutation(capture, "RemoveSample", null);
        foreach (var sampleBatch in new[] { s.Batch(captureAgain, 2), correction, sampleRemoval }) {
            var candidate = sampleBatch with { ExpectedDraftVersion = 2, Operations = sampleBatch.Operations.Select(x => x with { BaseDraftVersion = 2 }).ToList() };
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", candidate)).StatusCode);
        }
        await s.Accept(captureBatch with { SourceProofs = [] });
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(s.Root + "/operation-batches",
            s.Batch(captureAgain with { OperationId = receipt.OperationId }, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(s, 2) with {
            Operation = Input(s, 2).Operation with { OperationId = capture.OperationId } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(s, 2) with { BatchId = captureBatch.BatchId })).StatusCode);
        await Counts(s, 2, 1, 1, 1, 2);
        var other = output.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        var lastInput = Input(s, 2, other);
        var last = await Accept(s, lastInput with { Operation = lastInput.Operation with { ConfirmationSource = "Equipment" } }); Assert.True(last.Operation.WarnsLastApplicableItemRemoval);
        Assert.Equal(0, last.Operation.RemainingApplicableItemCount); Assert.True((await s.Read())!.AllItemsRemoved);
        Assert.All((await s.Read())!.Items!, x => { Assert.Equal("Removed", x.Disposition); Assert.Equal(1, x.MinimumSampleCount); });
        await Counts(s, 3, 2, 1, 1, 3); Assert.Equal(history, await Retained(f));
        var task = await f.Read(db => db.PatrolTasks.AsNoTracking().SingleAsync(x => x.Id == s.TaskId));
        Assert.Equal("PendingInspection", task.Status); Assert.Equal(0, task.CurrentRevisionNo); Assert.Null(task.StartedAtUtc);
        Assert.Null(task.SubmittedAtUtc); Assert.Null(task.OverallInspectionResult);
        await f.Edit(async db => (await db.PatrolTasks.SingleAsync(x => x.Id == s.TaskId)).Status = "Completed");
        Assert.Equal(receipt, await s.Client().GetFromJsonAsync<ItemRemovalReceipt>(s.Root + $"/operations/{receipt.OperationId}/receipt"));
        Assert.Equal(receipt, (await Accept(s, b with { SourceProof = null })).Operation);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), Input(s, 3))).StatusCode);
    }

    [Theory][InlineData("long-reason")][InlineData("control")][InlineData("unconfirmed")][InlineData("confirmation")]
    [InlineData("kind")][InlineData("version")][InlineData("item")][InlineData("attempt")][InlineData("foreign-attempt")]
    [InlineData("line")][InlineData("environment")][InlineData("workflow")][InlineData("base")][InlineData("expected")]
    public async Task RM02_03_13_Invalid_requests_make_no_fact(string kind)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Invalid(f, kind); }
    internal static async Task Invalid(PersonalSessionFixture f, string kind) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var o = b.Operation;
        b = kind switch {
            "long-reason" => b with { Operation = o with { Reason = new string('x', 501) } },
            "control" => b with { Operation = o with { Reason = "absent\0" } },
            "unconfirmed" => b with { Operation = o with { ConfirmedAbsentOrNotApplicable = false } },
            "confirmation" => b with { Operation = o with { ConfirmationSource = "IPQA" } },
            "kind" => b with { Operation = o with { Kind = "UnableToMeasureNG" } },
            "version" => b with { Operation = o with { CanonicalizationVersion = 2 } },
            "item" => b with { Operation = o with { TaskItemId = Guid.NewGuid() } },
            "attempt" => b with { Operation = o with { InspectionAttemptId = Guid.NewGuid() } },
            "foreign-attempt" => b with { Operation = o with { InspectionAttemptId = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId).InspectionAttemptId } },
            "line" => b with { ExpectedTaskLineId = f.Line2 }, "environment" => b with { ExpectedEnvironmentId = "other" },
            "workflow" => b with { ExpectedWorkflowVersion = 1 }, "base" => b with { Operation = o with { BaseDraftVersion = 1 } },
            "expected" => b with { ExpectedDraftVersion = 1, Operation = o with { BaseDraftVersion = 1 } }, _ => b
        };
        var response = await s.Client().PostAsJsonAsync(Url(s), b);
        Assert.Equal(kind is "long-reason" or "control" or "unconfirmed" or "confirmation" or "kind" or "version" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, response.StatusCode);
        await Counts(s, 0, 0, 0, 0, 0); Assert.Equal(0, await f.Read(db => db.SharedDrafts.CountAsync()));
    }

    [Fact] public async Task RM04_06_13_Auth_before_binding_and_no_bulk_or_fake_sample_fields()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }
    internal static async Task Authority(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var url = Url(s);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsync(url, new StringContent("{broken", Encoding.UTF8, "application/json"))).StatusCode);
        foreach (var name in new[] { "admin", "pqe" })
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Client((await f.Login(name)).AccessToken).PostAsJsonAsync(url, b)).StatusCode);
        var noFamily = f.Token(s.B, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(noFamily).PostAsJsonAsync(url, b)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(s.Source.ProofSecret).PostAsJsonAsync(url, b)).StatusCode);
        using var scope = f.Factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        var fake = new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims));
        Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.RemoveItemAsync(s.TaskId, b, fake, default))).Status);
        var json = JsonSerializer.Serialize(b, DraftCanonical.Json);
        foreach (var bad in new[] { json.Insert(1, "\"operations\":[],"), json.Insert(1, "\"sampleId\":\"" + Guid.NewGuid() + "\","),
            json.Insert(1, "\"BATCHID\":\"" + Guid.NewGuid() + "\",") })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().PostAsync(url, new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        var legacy = await f.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().PostAsJsonAsync($"/api/line-patrol/tasks/{legacy}/item-removal-batches", b)).StatusCode);
        await f.Edit(async db => { var t = await db.PatrolTasks.SingleAsync(x => x.Id == s.TaskId); t.PlanningSnapshotJson = null; t.PlanningSnapshotVersion = null; });
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(url, b)).StatusCode);
        Assert.Equal(0, await f.Read(db => db.ItemRemovalOperations.CountAsync())); Assert.Equal(0, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
    }

    [Theory][InlineData("proof")][InlineData("owner")][InlineData("missing")][InlineData("unknown")]
    public async Task RM06_Invalid_source_quarantines_original_identity(string kind)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await BadSource(f, kind); }
    internal static async Task BadSource(PersonalSessionFixture f, string kind) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s);
        b = kind switch {
            "proof" => b with { SourceProof = b.SourceProof! with { ProofSecret = new string('x', 43) } },
            "owner" => b with { Operation = b.Operation with { ContributorUserIdAssertion = TestAccounts.Ipqa2Id } },
            "missing" => b with { SourceProof = null },
            _ => b with { Operation = b.Operation with { SourceWorkContextId = Guid.NewGuid() }, SourceProof = null }
        };
        var response = await s.Client().PostAsJsonAsync(Url(s), b); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(problem.GetProperty("quarantineRequired").GetBoolean()); Assert.True(problem.GetProperty("preserveOriginals").GetBoolean());
        Assert.Equal(b.Operation.OperationId, problem.GetProperty("operationIds")[0].GetGuid());
        await Counts(s, 0, 0, 0, 0, 0);
    }
}
