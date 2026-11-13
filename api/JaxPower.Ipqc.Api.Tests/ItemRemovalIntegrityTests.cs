using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ItemRemovalIntegrityTests
{
    [Fact] public async Task RM15_Full_200_character_environment_contract_is_persisted()
    { await using var f = await PersonalSessionFixture.CreateAsync(environment: new string('e', 200)); await LongEnvironment(f); }
    internal static async Task LongEnvironment(PersonalSessionFixture f) {
        var task = await WorkContextFixture.Task(f); var a = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var scopes = (await f.Client(b.AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!;
        var response = await f.Client(a.AccessToken).PostAsJsonAsync(WorkContextFixture.Root(task), new WorkContextIssueInput(f.Line1, 2, f.Options.EnvironmentId!));
        response.EnsureSuccessStatusCode(); var source = (await response.Content.ReadFromJsonAsync<WorkContextIssued>())!;
        var item = scopes.Items[0]; var s = new DraftSetup(f, task, item.TaskItemId, item.InspectionAttemptId, a, b, source);
        var result = await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s) with { ExpectedEnvironmentId = f.Options.EnvironmentId! });
        Assert.Equal(f.Options.EnvironmentId, result.Operation.EnvironmentId);
        Assert.Equal(f.Options.EnvironmentId, await f.Read(db => db.ItemRemovalOperations.Select(x => x.EnvironmentId).SingleAsync()));
    }
    [Fact] public async Task RM06_Previously_accepted_A_history_survives_B_removal_after_A_revocation()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await AcceptedA(f); }
    internal static async Task AcceptedA(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var capture = Assert.Single((await s.Accept(s.Batch(s.Capture()))).Operations);
        var history = await ItemRemovalTests.Retained(f); await SharedDraftFixture.Revoke(s);
        var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B); var b = ItemRemovalTests.Input(s, 1);
        b = b with { Operation = b.Operation with { SourceWorkContextId = sourceB.Context.Id, ContributorUserIdAssertion = sourceB.Context.UserId },
            SourceProof = new(sourceB.Context.Id, sourceB.ProofSecret) };
        var removed = await ItemRemovalTests.Accept(s, b); Assert.Equal(s.B.User.Id, removed.Operation.ContributorUserId);
        Assert.Equal(history, await ItemRemovalTests.Retained(f));
        Assert.Equal(capture, await s.Client().GetFromJsonAsync<DraftOperationReceipt>(s.Root + $"/operations/{capture.OperationId}/receipt"));
        await ItemRemovalTests.Counts(s, 2, 1, 1, 1, 2);
    }
    [Fact] public async Task RM09_14_Native_immutability_joint_scope_and_cross_kind_ID_uniqueness()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Integrity(f); }
    internal static async Task Integrity(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var scopes = (await s.Read())!.CurrentAttempts;
        var other = scopes.Single(x => x.TaskItemId != s.ItemId);
        var op = s.Capture() with { TaskItemId = other.TaskItemId, InspectionAttemptId = other.InspectionAttemptId };
        await s.Accept(s.Batch(op));
        var removal = (await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1))).Operation;
        foreach (var table in new[] { "ItemRemovalOperations", "SharedOperationIdentities" })
            foreach (var sql in new[] { $"UPDATE \"{table}\" SET \"OperationId\"=\"OperationId\"", $"DELETE FROM \"{table}\"" }) {
                var ex = await Assert.ThrowsAnyAsync<Exception>(() => f.Edit(async db => await db.Database.ExecuteSqlRawAsync(sql)));
                SharedDraftIntegrityTests.Constraint(ex, f, "immutable");
            }
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.ItemRemovalOperations.SingleAsync()).ReceiptJson = "{}"));
        Assert.Contains("immutable", tracked.Message);
        var original = await f.Read(db => db.ItemRemovalOperations.AsNoTracking().SingleAsync());
        ItemRemovalOperation Clone() => JsonSerializer.Deserialize<ItemRemovalOperation>(JsonSerializer.Serialize(original))!;
        var wrongOwner = Clone(); wrongOwner.OperationId = Guid.NewGuid(); wrongOwner.ContributorUserId = TestAccounts.Ipqa2Id;
        var scope = await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db => { db.ItemRemovalOperations.Add(wrongOwner); return Task.CompletedTask; }));
        SharedDraftIntegrityTests.Constraint(scope, f, "scope mismatch");
        // Other native constraints are valid; the global cross-ledger PK must be the rejecting constraint.
        var sample = await f.Read(db => db.ContributionOperations.AsNoTracking().SingleAsync());
        var originalSampleOperationId = sample.OperationId;
        sample.OperationId = removal.OperationId;
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db => { db.ContributionOperations.Add(sample); return Task.CompletedTask; }));
        Assert.Contains("SharedOperationIdentities", duplicate.GetBaseException().Message);
        var reverse = Clone(); reverse.OperationId = originalSampleOperationId; reverse.BatchId = sample.BatchId;
        var reverseFailure = await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db => { db.ItemRemovalOperations.Add(reverse); return Task.CompletedTask; }));
        Assert.Contains("SharedOperationIdentities", reverseFailure.GetBaseException().Message);
        var duplicateAttempt = Clone(); duplicateAttempt.OperationId = Guid.NewGuid(); duplicateAttempt.BatchId = sample.BatchId; duplicateAttempt.DraftVersion = 1;
        var failed = await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db => { db.ItemRemovalOperations.Add(duplicateAttempt); return Task.CompletedTask; }));
        Assert.Contains("InspectionAttemptId", failed.GetBaseException().Message);
        Assert.False(await f.Read(db => db.SharedOperationIdentities.AnyAsync(x => x.OperationId == duplicateAttempt.OperationId)));
        await ItemRemovalTests.Counts(s, 2, 1, 1, 1, 2);
    }

    [Fact] public async Task RM06_09_Canonical_bytes_and_redaction_do_not_include_transport_or_sample_IDs()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Canonical(f); }
    internal static async Task Canonical(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = ItemRemovalTests.Input(s, reason: "汉字é");
        var canonical = DraftCanonical.ItemRemoval(s.TaskId, b);
        Assert.Equal(canonical, DraftCanonical.ItemRemoval(s.TaskId, b with { SourceProof = null, BatchId = Guid.NewGuid(), ExpectedDraftVersion = 9 }));
        Assert.NotEqual(canonical, DraftCanonical.ItemRemoval(s.TaskId, b with { Operation = b.Operation with { Reason = "汉字e\u0301" } }));
        Assert.NotEqual(DraftCanonical.ItemRemoval(s.TaskId, b with { Operation = b.Operation with { Reason = null } }),
            DraftCanonical.ItemRemoval(s.TaskId, b with { Operation = b.Operation with { Reason = "" } }));
        Assert.StartsWith("{\"domain\":\"item-removal\",\"v\":1,", canonical);
        Assert.Contains("\\u6C49\\u5B57", canonical); Assert.DoesNotContain("sample", canonical, StringComparison.OrdinalIgnoreCase);
        var accepted = await ItemRemovalTests.Accept(s, b);
        var business = await f.Read(async db => JsonSerializer.Serialize(new { Ledger = await db.ItemRemovalOperations.AsNoTracking().ToListAsync(),
            Batches = await db.AcceptedBatchReceipts.AsNoTracking().ToListAsync(), Audits = await db.SharedAcceptanceAudits.AsNoTracking().ToListAsync() }));
        var response = JsonSerializer.Serialize(accepted, DraftCanonical.Json); business += response;
        foreach (var secret in new[] { s.Source.ProofSecret, DraftCanonical.Hash(s.Source.ProofSecret), s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken! })
            Assert.DoesNotContain(secret, business);
        Assert.DoesNotContain("sampleId", JsonSerializer.Serialize(accepted.Operation, DraftCanonical.Json), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(s.B.PersonalSessionId!.Value.ToString("D"), canonical);
    }
}
