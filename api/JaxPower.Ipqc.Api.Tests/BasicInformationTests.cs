using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.BasicInformationFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationTests
{
    [Fact] public async Task Real_optional_fields_and_correction_preserve_individual_A_B_provenance() { await using var f = await PersonalSessionFixture.CreateAsync(); await Lifecycle(f); }
    internal static async Task Lifecycle(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var before = await Facts(f); var taskBefore = await f.Read(db => db.PatrolTasks.Select(x => new { x.Status, x.CurrentRevisionNo, x.Shift, x.UpdatedAtUtc, x.StartedAtUtc, x.SubmittedAtUtc, x.OverallInspectionResult }).SingleAsync());
        var initial = await Read(s); Assert.Equal(0, initial.DraftVersion); Assert.Equal(0, initial.AcceptedFieldCount); Assert.Equal(0, initial.NonblankFieldCount);
        Assert.Equal(new(null, null, null), initial.Values); Assert.All(initial.Fields, x => { Assert.Equal("Unrecorded", x.ValueState); Assert.False(x.HasAcceptedFact); Assert.Null(x.LastOperation); });
        Assert.Empty(initial.History); Assert.Equal(before, await Facts(f)); Assert.Equal(0, await f.Read(db => db.SharedDrafts.CountAsync()));
        var customer = (await Accept(s, Input(s))).Operation;
        var production = (await Accept(s, Input(s, 1, BasicInformationField.Production, "Synthetic free text production"))).Operation;
        var bSource = await WorkContextFixture.Issue(f, s.TaskId, s.B);
        var mo = (await Accept(s, Input(s, 2, BasicInformationField.ProductMo, "Synthetic MO", source: bSource), s.A)).Operation;
        Assert.Equal(s.A.User.Id, customer.ContributorUserId); Assert.Equal(s.B.User.Id, customer.ActorUserId); Assert.Equal(s.B.PersonalSessionId, customer.ActorPersonalSessionId);
        Assert.Equal(s.B.User.Id, mo.ContributorUserId); Assert.Equal(s.A.User.Id, mo.ActorUserId); Assert.Equal(s.A.PersonalSessionId, mo.ActorPersonalSessionId);
        var corrected = (await Accept(s, Input(s, 3, value: "Explicit B correction", previous: customer, source: bSource))).Operation;
        var r = await Read(s); Assert.Equal(4, r.DraftVersion); Assert.Equal(3, r.AcceptedFieldCount); Assert.Equal(3, r.NonblankFieldCount);
        Assert.Equal(new("Explicit B correction", "Synthetic free text production", "Synthetic MO"), r.Values);
        Assert.Equal(corrected, r.Fields.Single(x => x.Field == BasicInformationField.Customer).LastOperation);
        Assert.Equal(production, r.Fields.Single(x => x.Field == BasicInformationField.Production).LastOperation);
        Assert.Equal(mo, r.Fields.Single(x => x.Field == BasicInformationField.ProductMo).LastOperation);
        Assert.Equal(customer, await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, customer.OperationId)));
        Assert.Equal(new[] { corrected, mo, production, customer }, r.History); Assert.Equal(new(customer.OperationId, customer.ContentHash), corrected.Predecessor);
        var row = await f.Read(db => db.TaskHeaderOperations.AsNoTracking().SingleAsync(x => x.OperationId == corrected.OperationId));
        Assert.Equal(corrected.ContentHash, DraftCanonical.Hash(row.CanonicalPayload)); Assert.Equal(corrected.Value, row.Value); Assert.Equal(corrected.ActorUserId, row.ActorUserId);
        var proofHash = await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.ProofSecretHash).SingleAsync());
        var json = JsonSerializer.Serialize(r); foreach (var secret in new[] { s.Source.ProofSecret, proofHash, s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken! }) Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain(s.Source.ProofSecret, row.CanonicalPayload); Assert.DoesNotContain(proofHash, row.CanonicalPayload);
        before = await Facts(f); _ = await Read(s); _ = await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, corrected.OperationId)); Assert.Equal(before, await Facts(f));
        Assert.Equal(taskBefore, await f.Read(db => db.PatrolTasks.Select(x => new { x.Status, x.CurrentRevisionNo, x.Shift, x.UpdatedAtUtc, x.StartedAtUtc, x.SubmittedAtUtc, x.OverallInspectionResult }).SingleAsync()));
        await Counts(s, 4, 4, 3);
        before = await Facts(f); var preflight = (await s.Client().GetFromJsonAsync<SubmitPreflightOutput>(SubmitPreflightTests.Url(s)))!;
        Assert.False(preflight.CanSubmit); Assert.False(preflight.CanExecute); Assert.Equal(4, preflight.DraftVersion); Assert.DoesNotContain(preflight.BlockingReasons, x => x.Contains("Header", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(SubmitPreflightTests.Url(s) + "?expectedDraftVersion=3")).StatusCode); Assert.Equal(before, await Facts(f));
    }
    [Theory][InlineData("null")][InlineData("empty")][InlineData("space")][InlineData("max")][InlineData("unicode")]
    public async Task Exact_nullable_empty_whitespace_and_scalar_boundaries_are_observable(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Values(f, kind); }
    internal static async Task Values(PersonalSessionFixture f, string kind) {
        var s = await SharedDraftFixture.Setup(f); var value = kind switch { "null" => null, "empty" => "", "space" => "   ", "max" => new string('x', 200), _ => string.Concat(Enumerable.Repeat("\U0001F50C", 200)) };
        var receipt = (await Accept(s, Input(s, value: value))).Operation; var r = await Read(s); var field = r.Fields[0];
        Assert.Equal(value, field.Value); Assert.True(field.HasAcceptedFact); Assert.Equal(receipt, field.LastOperation);
        Assert.Equal(kind switch { "null" => "ExplicitNull", "empty" => "Empty", "space" => "Whitespace", _ => "Provided" }, field.ValueState);
        Assert.Equal(kind is "max" or "unicode" ? 1 : 0, r.NonblankFieldCount);
        var clear = (await Accept(s, Input(s, 1, value: null, previous: receipt))).Operation;
        Assert.Equal("ExplicitNull", (await Read(s)).Fields[0].ValueState); Assert.Null(clear.Value); Assert.Equal(receipt, await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, receipt.OperationId)));
        await Counts(s, 2, 2, 1);
    }
    [Theory][InlineData("long")][InlineData("unicode-long")][InlineData("control")][InlineData("newline")]
    public async Task Invalid_bounded_text_never_claims_a_version(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await InvalidValue(f, kind); }
    internal static async Task InvalidValue(PersonalSessionFixture f, string kind) {
        var s = await SharedDraftFixture.Setup(f); var before = await Facts(f);
        var value = kind switch { "long" => new string('x', 201), "unicode-long" => string.Concat(Enumerable.Repeat("\U0001F50C", 201)), "control" => "synthetic\0text", _ => "synthetic\ntext" };
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().PostAsJsonAsync(WriteUrl(s), Input(s, value: value))).StatusCode);
        Assert.Equal(before, await Facts(f)); await Counts(s, 0, 0, 0);
    }
    [Fact] public async Task Accepted_ID_replay_is_original_and_changed_identity_conflicts() { await using var f = await PersonalSessionFixture.CreateAsync(); await Replay(f); }
    internal static async Task Replay(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var first = await Accept(s, b); var before = await Facts(f);
        var replay = await Accept(s, b with { SourceProof = null }, s.A); Assert.True(replay.ReplayOnly); Assert.Equal(first.Operation, replay.Operation);
        Assert.Equal(JsonSerializer.Serialize(first.BatchReceipt), JsonSerializer.Serialize(replay.BatchReceipt));
        var operationReplay = await Accept(s, b with { BatchId = Guid.NewGuid(), SourceProof = null }, s.A);
        Assert.True(operationReplay.ReplayOnly); Assert.Null(operationReplay.BatchReceipt); Assert.Equal(first.Operation, operationReplay.Operation);
        Assert.Equal(JsonSerializer.Serialize(first.BatchReceipt), JsonSerializer.Serialize(await s.Client().GetFromJsonAsync<DraftBatchReceipt>(s.Root + $"/operation-batches/{b.BatchId}/receipt"))); Assert.Equal(before, await Facts(f));
        var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B); before = await Facts(f);
        foreach (var changed in new[] {
            b with { Operation = b.Operation with { Value = "different" } },
            b with { Operation = b.Operation with { Field = BasicInformationField.Production } },
            b with { Operation = b.Operation with { SourceWorkContextId = sourceB.Context.Id, ContributorUserIdAssertion = sourceB.Context.UserId }, SourceProof = new(sourceB.Context.Id, sourceB.ProofSecret) },
            b with { ExpectedDraftVersion = 1 }, b with { CanonicalizationVersion = 2 },
            b with { Operation = b.Operation with { CanonicalizationVersion = 2 } }, b with { ExpectedEnvironmentId = "different environment" }, b with { ExpectedTaskLineId = f.Line2 }
        }) Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(WriteUrl(s), changed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(WriteUrl(s), b with { BatchId = Guid.NewGuid(), Operation = b.Operation with { Value = "different" } })).StatusCode);
        Assert.Equal(before, await Facts(f)); await Counts(s, 1, 1, 1);
    }
    [Fact] public async Task A_real_committed_lost_response_recovers_without_reapplication() { await using var f = await PersonalSessionFixture.CreateAsync(); await LostResponse(f); }
    internal static async Task LostResponse(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); f.CommitFault.AfterCommit = () => throw new IOException("Synthetic response lost after actual commit");
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(WriteUrl(s), b)).StatusCode);
        var before = await Facts(f); var receipt = (await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, b.Operation.OperationId)))!;
        var batch = (await s.Client().GetFromJsonAsync<DraftBatchReceipt>(s.Root + $"/operation-batches/{b.BatchId}/receipt"))!;
        Assert.Equal(receipt, Assert.Single(batch.HeaderOperations!)); Assert.Equal("Synthetic customer", (await Read(s)).Values.Customer);
        var replay = await Accept(s, b with { SourceProof = null }, s.A); Assert.True(replay.ReplayOnly); Assert.Equal(receipt, replay.Operation); Assert.Equal(s.B.User.Id, replay.Operation.ActorUserId);
        Assert.Equal(before, await Facts(f)); await Counts(s, 1, 1, 1);
    }
    [Fact] public async Task Exact_field_predecessors_and_shared_version_prevent_silent_replacements() { await using var f = await PersonalSessionFixture.CreateAsync(); await Predecessor(f); }
    internal static async Task Predecessor(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var customer = (await Accept(s, Input(s))).Operation;
        var production = (await Accept(s, Input(s, 1, BasicInformationField.Production, "Text production"))).Operation;
        var correct = (await Accept(s, Input(s, 2, previous: customer))).Operation; var before = await Facts(f);
        foreach (var bad in new[] { Input(s, 3), Input(s, 3, previous: customer), Input(s, 3, previous: production),
            Input(s, 3, BasicInformationField.ProductMo, previous: correct), Input(s, 2, previous: correct),
            Input(s, 3, previous: correct) with { Operation = Input(s, 3, previous: correct).Operation with { Predecessor = new(correct.OperationId, new string('A',64)) } } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(WriteUrl(s), bad)).StatusCode);
        Assert.Equal(before, await Facts(f)); await Counts(s, 3, 3, 2);
    }
    [Fact] public async Task Optional_field_history_pagination_versions_and_no_GET_writes_are_exact() { await using var f = await PersonalSessionFixture.CreateAsync(); await Paging(f); }
    internal static async Task Paging(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var last = (await Accept(s, Input(s))).Operation;
        for (var n = 1; n <= 5; n++) last = (await Accept(s, Input(s, n, value: "Correction " + n, previous: last))).Operation;
        var before = await Facts(f); var one = await Read(s, "?limit=2&expectedDraftVersion=6"); var two = await Read(s, "?limit=2&beforeDraftVersion=" + one.HistoryBeforeDraftVersion);
        var three = await Read(s, "?limit=2&beforeDraftVersion=" + two.HistoryBeforeDraftVersion);
        Assert.Equal(new long[] { 6, 5, 4, 3, 2, 1 }, one.History.Concat(two.History).Concat(three.History).Select(x => x.DraftVersion)); Assert.Null(three.HistoryBeforeDraftVersion);
        Assert.Equal(one.Values, two.Values); Assert.Equal(one.Values, three.Values); Assert.Equal(last, two.Fields[0].LastOperation);
        foreach (var query in new[] { "?limit=0", "?limit=101", "?beforeDraftVersion=0", "?expectedDraftVersion=-1", $"?expectedTaskLineId={Guid.Empty}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(ReadUrl(s) + query)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(ReadUrl(s) + "?expectedDraftVersion=5")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(ReceiptUrl(s, Guid.NewGuid()))).StatusCode);
        Assert.Equal(before, await Facts(f)); await Counts(s, 6, 6, 1);
    }
}
