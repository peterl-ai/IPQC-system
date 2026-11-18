using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.OrdinaryNgDetailsFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OrdinaryNgDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Optional_details_preserve_identity_original_values_and_formal_boundaries(bool quantitative)
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Lifecycle(f, quantitative);
    }

    internal static async Task Lifecycle(PersonalSessionFixture f, bool quantitative)
    {
        var s = await SharedDraftFixture.Setup(f, quantitative, sampleCount: "3");
        var sample = await DraftEvidenceAssessmentTests.Sample(s, value: quantitative ? 3m : null);
        Assert.Equal("NG", sample.Value!.Judgment);
        var before = await Facts(f);
        var observation = await Item(s);
        Assert.Equal("OrdinaryNG", observation.CurrentState);
        Assert.Null(observation.Latest);
        Assert.Equal(before, await Facts(f));
        var caseBefore = await s.Client().GetFromJsonAsync<SubmitPreflightOutput>(SubmitPreflightTests.Url(s));
        Assert.NotNull(caseBefore);
        Assert.False(caseBefore.CanSubmit);

        var request = Input(s, reason: "  e\u0301 \U0001F642  ");
        using var missing = await s.Client().GetAsync(ReceiptUrl(s, request.Operation.OperationId));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var created = await Accept(s, request);
        var first = created.Operation;
        var originalWire = await s.Client().GetStringAsync(ReceiptUrl(s, first.OperationId));
        var originalCommonWire = await s.Client().GetStringAsync(CommonUrl(s, request.BatchId));
        Assert.False(created.ReplayOnly);
        Assert.Equal(8, created.BatchReceipt!.ReceiptVersion);
        Assert.Equal(first.DraftVersion, created.BatchReceipt.DraftVersion);
        Assert.Equal(2, first.DraftVersion);
        Assert.Equal(s.A.User.Id, first.ContributorUserId);
        Assert.Equal(s.B.User.Id, first.ActorUserId);
        Assert.Equal(s.B.PersonalSessionId, first.ActorPersonalSessionId);
        Assert.Equal(s.Source.Context.Id, first.SourceWorkContextId);
        Assert.Equal(sample.ContentHash, Assert.Single(first.AcceptedNgBasis).ContentHash);
        Assert.Equal(sample.OperationId, Assert.Single(first.AcceptedNgBasis).OperationId);
        Assert.Equal(request.Operation.NgReason, first.NgReason);
        Assert.Null(first.NgType);
        Assert.Null(first.Remarks);
        Assert.True((await Item(s)).Latest!.IsCurrentlyApplicable);

        before = await Facts(f);
        var replay = await Accept(s, request with { SourceProof = null });
        Assert.True(replay.ReplayOnly);
        Assert.Equal(Json(created.BatchReceipt), Json(replay.BatchReceipt));
        Assert.Equal(Json(first), Json(replay.Operation));
        var opOnly = await Accept(s, request with { BatchId = Guid.NewGuid(), SourceProof = null });
        Assert.True(opOnly.ReplayOnly);
        Assert.Null(opOnly.BatchReceipt);
        Assert.Equal(before, await Facts(f));

        var changed = request with { Operation = request.Operation with { NgReason = first.NgReason!.Trim() } };
        await Reject(s, changed, HttpStatusCode.Conflict, "BatchIdentityConflict");
        await Reject(s, changed with { BatchId = Guid.NewGuid() }, HttpStatusCode.Conflict, "OperationIdentityConflict");
        await Reject(s, Input(s, 2), HttpStatusCode.Conflict, "OrdinaryNgPredecessorConflict");
        var correction = Input(s, 2, first, reason: null, type: "", remarks: "\u3000 ");
        await Reject(s, correction with { Operation = correction.Operation with { Predecessor = new(Guid.NewGuid(), first.ContentHash) } },
            HttpStatusCode.Conflict, "OrdinaryNgPredecessorConflict");
        await Reject(s, correction with { Operation = correction.Operation with { Predecessor = new(first.OperationId, new string('0', 64)) } },
            HttpStatusCode.Conflict, "OrdinaryNgPredecessorConflict");
        Assert.Equal(before, await Facts(f));

        var edited = (await Accept(s, correction)).Operation;
        Assert.Empty(edited.NgType!);
        Assert.Equal("\u3000 ", edited.Remarks);
        Assert.Null(edited.NgReason);
        before = await Facts(f);
        await Reject(s, Input(s, 3, first), HttpStatusCode.Conflict, "OrdinaryNgPredecessorConflict");
        Assert.Equal(before, await Facts(f));
        var cleared = (await Accept(s, Input(s, 3, edited, reason: null))).Operation;
        Assert.Null(cleared.NgReason);
        Assert.Null(cleared.NgType);
        Assert.Null(cleared.Remarks);
        Assert.Equal(4, cleared.DraftVersion);
        Assert.True((await Item(s)).Latest!.IsCurrentlyApplicable);
        Assert.Equal(originalWire, await s.Client().GetStringAsync(ReceiptUrl(s, first.OperationId)));
        Assert.Equal(originalCommonWire, await s.Client().GetStringAsync(CommonUrl(s, request.BatchId)));

        var newest = await Read(s, "?historyLimit=1&expectedDraftVersion=4");
        var cursor = Assert.IsType<long>(newest.HistoryBeforeDraftVersion);
        var previous = await Read(s, "?historyLimit=1&beforeDraftVersion=" + cursor);
        Assert.Equal(edited.OperationId, Assert.Single(previous.History).Receipt.OperationId);
        Assert.False(previous.History[0].IsCurrentlyApplicable);
        Assert.Equal(cleared.OperationId, previous.Items.Single(i => i.TaskItemId == s.ItemId).Latest!.Receipt.OperationId);
        Assert.Empty((await Read(s, "?beforeDraftVersion=2")).History);
        Assert.Equal(cleared.OperationId, (await Item(s, "?beforeDraftVersion=2")).Latest!.Receipt.OperationId);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(ReadUrl(s) + "?expectedDraftVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(ReadUrl(s) + "?historyLimit=101")).StatusCode);
        Assert.DoesNotContain(s.Source.ProofSecret, Json(newest));
        Assert.DoesNotContain(s.B.AccessToken, Json(newest));
        var preflight = (await s.Client().GetFromJsonAsync<SubmitPreflightOutput>(SubmitPreflightTests.Url(s)))!;
        Assert.False(preflight.CanExecute); Assert.False(preflight.CanSubmit);
        Assert.Equal(caseBefore.BlockingReasons, preflight.BlockingReasons);
        Assert.Equal(3, (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId == s.ItemId).MinimumSampleCount);
        await s.AssertCounts(4, 1, 1, 4);
        Assert.Equal(1, await f.Read(db => db.InspectionAttempts.CountAsync(x => x.PatrolTaskItemId == s.ItemId)));
    }

    [Fact]
    public async Task Real_basis_changes_and_Removed_preserve_original_metadata_without_silent_carry_forward()
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await BasisChanges(f);
    }

    internal static async Task BasisChanges(PersonalSessionFixture f)
    {
        var s = await SharedDraftFixture.Setup(f);
        await Reject(s, Input(s, 0), HttpStatusCode.Conflict, "OrdinaryNgItemNotEligible");
        var sample = await DraftEvidenceAssessmentTests.Sample(s);
        var first = (await Accept(s, Input(s))).Operation;
        async Task<DraftOperationReceipt> Correct(DraftOperationReceipt old, string judgment, long version)
        {
            var batch = s.Mutation(old, "CorrectSample", new(judgment, null, null));
            var operation = batch.Operations[0] with { BaseDraftVersion = version };
            return (await s.Accept(batch with { ExpectedDraftVersion = version, Operations = [operation] }))
                .Operations.Single(x => x.OperationId == operation.OperationId);
        }

        var newerNg = await Correct(sample, "NG", 2);
        var afterNg = (await Item(s)).Latest!;
        Assert.True(afterNg.HasCurrentOrdinaryNg);
        Assert.False(afterNg.MatchesAcceptedNgBasis);
        Assert.False(afterNg.IsCurrentlyApplicable);
        var ok = await Correct(newerNg, "OK", 3);
        var afterOk = (await Item(s)).Latest!;
        Assert.False(afterOk.HasCurrentOrdinaryNg);
        Assert.False(afterOk.IsCurrentlyApplicable);
        Assert.Equal("NotCurrentlyNG", afterOk.CurrentState);
        Assert.Equal(Json(first), Json(afterOk.Receipt));
        await Reject(s, Input(s, 4, first), HttpStatusCode.Conflict, "OrdinaryNgItemNotEligible");

        var ng = await Correct(ok, "NG", 4);
        Assert.False((await Item(s)).Latest!.IsCurrentlyApplicable);
        var updated = (await Accept(s, Input(s, 5, first))).Operation;
        Assert.Equal(ng.OperationId, Assert.Single(updated.AcceptedNgBasis).OperationId);
        Assert.True((await Item(s)).Latest!.IsCurrentlyApplicable);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 6));
        Assert.Equal("Removed", (await Item(s)).CurrentState);
        Assert.False((await Item(s)).Latest!.IsCurrentlyApplicable);
        await Reject(s, Input(s, 7, updated), HttpStatusCode.Conflict, "OrdinaryNgItemNotEligible");
        Assert.Equal(Json(first), await s.Client().GetStringAsync(ReceiptUrl(s, first.OperationId)));
    }

    [Fact]
    public async Task UnableToMeasure_has_its_own_reason_and_no_sample_or_ordinary_metadata()
    {
        await using var p = await PhotoFixture.Create();
        await Unable(p);
    }

    internal static async Task Unable(PhotoFixture p)
    {
        var s = await SharedDraftFixture.Setup(p.F);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var setup = new UnableSetup(s, photo, reference!);
        var result = (await UnableToMeasureTests.Accept(setup, UnableToMeasureTests.Input(setup))).Operation;
        var snapshot = await Facts(p.F);
        Assert.Equal("UnableToMeasureNG", (await Item(s)).CurrentState);
        Assert.Null((await Item(s)).Latest);
        await Reject(s, Input(s, result.DraftVersion), HttpStatusCode.Conflict, "OrdinaryNgItemNotEligible");
        Assert.Equal(snapshot, await Facts(p.F));
        Assert.False(string.IsNullOrWhiteSpace(result.NgReason));
        Assert.Empty(await p.F.Read(db => db.SharedSamples.ToListAsync()));
    }

    [Fact]
    public async Task Strict_json_and_exact_text_bounds_are_enforced_before_acceptance()
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Shape(f);
    }

    internal static async Task Shape(PersonalSessionFixture f)
    {
        var s = await SharedDraftFixture.Setup(f);
        await DraftEvidenceAssessmentTests.Sample(s);
        var baseline = await Facts(f);
        var request = Input(s, reason: null);
        var json = Json(request);
        var unknown = JsonNode.Parse(json)!;
        unknown["operation"]!["judgment"] = "NG";
        var bad = new[] { unknown.ToJsonString(),
            json.Replace("\"ngReason\":null", "\"NGREASON\":null,\"ngReason\":null"),
            json.Replace("\"baseDraftVersion\":1", "\"baseDraftVersion\":\"1\""),
            json.Replace("\"ngReason\":null", "\"ngReason\":\"\\ud800\""),
            json.Replace("\"ngReason\":null", "\"ngReason\":\"synthetic\\u0000reason\"") };
        foreach (var body in bad)
        {
            Assert.NotEqual(json, body);
            using var response = await s.Client().PostAsync(WriteUrl(s), new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await Reject(s, Input(s, remarks: new string('x', 2001)), HttpStatusCode.BadRequest, "InvalidOrdinaryNgText");
        Assert.Equal(baseline, await Facts(f));
        var omitted = JsonNode.Parse(json)!;
        foreach (var field in new[] { "ngType", "ngReason", "remarks" }) omitted["operation"]!.AsObject().Remove(field);
        using var empty = await s.Client().PostAsync(WriteUrl(s), new StringContent(omitted.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var accepted = (await empty.Content.ReadFromJsonAsync<OrdinaryNgDetailsBatchResult>())!.Operation;
        Assert.Null(accepted.NgReason);
        var limit = new string('x', 1998) + "\U0001F642";
        Assert.Equal(limit, (await Accept(s, Input(s, 2, accepted, remarks: limit))).Operation.Remarks);
    }

    [Fact]
    public async Task Current_family_and_exact_scope_are_required_but_replay_survives_source_revocation()
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        await Authority(f);
    }

    internal static async Task Authority(PersonalSessionFixture f)
    {
        var s = await SharedDraftFixture.Setup(f);
        await DraftEvidenceAssessmentTests.Sample(s);
        var request = Input(s);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(ReadUrl(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(s.Source.ProofSecret).PostAsJsonAsync(WriteUrl(s), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(f.Token(s.B, x => x.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()))
            .PostAsJsonAsync(WriteUrl(s), request)).StatusCode);
        foreach (var name in new[] { "pqe", "admin" })
        {
            var outside = s.Client(await f.Login(name));
            Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(ReadUrl(s))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(ReceiptUrl(s, request.Operation.OperationId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsJsonAsync(WriteUrl(s), request)).StatusCode);
        }
        var before = await Facts(f);
        await Reject(s, request with { Operation = request.Operation with { ContributorUserIdAssertion = s.B.User.Id } },
            HttpStatusCode.Conflict, "Source");
        await Reject(s, request with { ExpectedEnvironmentId = "synthetic-wrong" }, HttpStatusCode.Conflict, "DraftScopeMismatch");
        await Reject(s, request with { ExpectedWorkflowVersion = 1 }, HttpStatusCode.Conflict, "DraftScopeMismatch");
        await Reject(s, request with { ExpectedTaskLineId = f.Line2 }, HttpStatusCode.Conflict, "Line");
        await Reject(s, request with { Operation = request.Operation with { InspectionAttemptId = Guid.NewGuid() } },
            HttpStatusCode.Conflict, "DraftAttemptConflict");
        var otherScope = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        await Reject(s, request with { Operation = request.Operation with { InspectionAttemptId = otherScope.InspectionAttemptId } },
            HttpStatusCode.Conflict, "DraftAttemptConflict");
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().PostAsJsonAsync(WriteUrl(s with { TaskId = Guid.NewGuid() }), request)).StatusCode);
        Assert.Equal(before, await Facts(f));
        var first = (await Accept(s, request)).Operation;
        await SharedDraftFixture.Revoke(s);
        before = await Facts(f);
        await Reject(s, Input(s, 2, first), HttpStatusCode.Conflict, "Source");
        Assert.Equal(Json(first), await s.Client().GetStringAsync(ReceiptUrl(s, first.OperationId)));
        Assert.True((await Accept(s, request with { SourceProof = null })).ReplayOnly);
        Assert.Equal(before, await Facts(f));
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(ReceiptUrl(s, first.OperationId))).StatusCode);
    }
}
