using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Contracts.PreflightRequirementState;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SubmitPreflightTests
{
    internal static string Url(DraftSetup s) => s.Root + "/shared-draft/submit-preflight";
    internal static async Task<DraftSetup> Setup(PhotoFixture p, string policy = "Optional", string minimum = "1", bool quantitative = false) {
        var f = p.F; f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero);
        var standard = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false);
        await f.Edit(async db => {
            foreach (var i in await db.PatrolStandardItems.Where(x => x.PatrolStandardId == standard).ToListAsync()) {
                i.SampleCount = minimum;
                if (quantitative) { i.InspectionType = "Quantitative"; i.LowerLimitOperator = ">="; i.LowerLimitValue = "1.5"; i.UpperLimitOperator = "<="; i.UpperLimitValue = "2.5"; }
            }
        });
        await PhotoPolicyFixture.ConfirmAll(f, standard, policy); await LineSharedPlanningFixture.Plan(f, standard);
        f.Clock.Now = f.Clock.Now.AddMinutes(1); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f));
        var task = Assert.Single(await LineSharedPlanningFixture.Tasks(f)); var a = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var scope = (await f.Client(b.AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task.Id)))!;
        var item = scope.Items[0]; return new(f, task.Id, item.TaskItemId, item.InspectionAttemptId, a, b, await WorkContextFixture.Issue(f, task.Id, a));
    }
    internal static async Task<SubmitPreflightOutput> Read(DraftSetup s, string query = "") {
        var response = await s.Client().GetAsync(Url(s) + query); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var r = (await response.Content.ReadFromJsonAsync<SubmitPreflightOutput>())!;
        Assert.False(r.CanExecute); Assert.False(r.CanSubmit); Assert.False(r.FormalSubmitPerformed); Assert.False(r.AcceptsFormalInspection);
        Assert.Equal("Blocked", r.Outcome); Assert.Equal("ReadOnlyPointInTime", r.Observation); Assert.Contains("FormalSubmitWorkflowNotImplemented", r.BlockingReasons);
        Assert.Equal("NotEvaluatedForFormalSubmission", r.Completeness); return r;
    }
    internal static SubmitPreflightItem Item(SubmitPreflightOutput r, DraftSetup s) => r.Items.Single(x => x.Evidence.TaskItemId == s.ItemId);
    internal static PreflightRequirementState State(SubmitPreflightItem i, string code) => i.Requirements.Single(x => x.Code == code).State;
    internal static async Task NoWrites(PhotoFixture p, DraftSetup s) {
        string Bytes() => JsonSerializer.Serialize(Directory.Exists(p.DirectoryPath) ? Directory.GetFiles(p.DirectoryPath, "*", SearchOption.AllDirectories).Order().Select(path =>
            new { Path = path, Modified = File.GetLastWriteTimeUtc(path), Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) }).ToList() : []);
        var before = await SampleEvidenceTests.Facts(p); var bytes = Bytes(); _ = await Read(s); Assert.Equal(before, await SampleEvidenceTests.Facts(p)); Assert.Equal(bytes, Bytes());
        Assert.Equal(0, await p.F.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.PatrolTaskReviews.CountAsync()));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Complete_ordinary_OK_observations_never_authorize_formal_Submit(bool quantitative) { await using var p = await PhotoFixture.Create(); await Ordinary(p, quantitative); }
    internal static async Task Ordinary(PhotoFixture p, bool quantitative) {
        var s = await Setup(p, minimum: "3", quantitative: quantitative);
        foreach (var scope in (await s.Read())!.CurrentAttempts) {
            var target = s with { ItemId = scope.TaskItemId, AttemptId = scope.InspectionAttemptId };
            for (var n = 0; n < 3; n++) await DraftEvidenceAssessmentTests.Sample(target, "OK", quantitative ? 2m : null);
        }
        var before = await SampleEvidenceTests.Facts(p); var r = await Read(s); Assert.True(r.PageObservationComplete); Assert.True(r.TaskObservationComplete); Assert.False(r.AllItemsRemoved);
        Assert.All(r.Items, i => {
            Assert.Equal(3, i.Evidence.MinimumSampleCount); Assert.Equal(3, i.Evidence.ActiveSampleCount); Assert.Equal(Satisfied, State(i, "FrozenMinimumSamples"));
            Assert.Equal(Satisfied, State(i, "CurrentSampleJudgments")); Assert.Equal(Satisfied, State(i, "CurrentPhotoEvidence")); Assert.Empty(i.Evidence.Photos);
            Assert.Equal("Optional", i.Evidence.PhotoPolicy.RecognizedText); Assert.True(i.Evidence.PhotoPolicy.HasExplicitConfirmation);
            Assert.All(i.Evidence.Samples, x => Assert.Equal(quantitative ? 2m : (decimal?)null, x.ValueOperation!.Value!.InspectionValue));
        });
        var existing = await DraftEvidenceAssessmentTests.Read(s);
        Assert.Equal(JsonSerializer.Serialize(existing.Items), JsonSerializer.Serialize(r.Items.Select(x => x.Evidence).ToList()));
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        var json = JsonSerializer.Serialize(r);
        foreach (var secret in new[] { s.Source.ProofSecret, s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, p.DirectoryPath }) Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("StorageKey", json); Assert.Equal(0, p.Fault.ReadCount); await NoWrites(p, s);
    }
    [Theory][InlineData("none")][InlineData("ready")][InlineData("pending")][InlineData("valid")]
    [InlineData("detached")][InlineData("missing")][InlineData("corrupt")][InlineData("unavailable")]
    public async Task Required_policy_demands_actual_accepted_current_bytes(string kind) { await using var p = await PhotoFixture.Create(); await Required(p, kind); }
    internal static async Task Required(PhotoFixture p, string kind) {
        var s = await Setup(p, "Required"); await DraftEvidenceAssessmentTests.Sample(s, "OK");
        if (kind != "none") {
            var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s, kind is not ("ready" or "pending"), kind != "pending");
            if (kind == "detached") await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, photo, await DraftEvidenceAssessmentTests.Version(s), previous: reference, kind: "DetachAttachment"));
            if (kind == "missing") File.Delete(await p.FinalPath(photo.AttachmentId));
            if (kind == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(photo.AttachmentId), [1, 2, 3]);
            if (kind == "unavailable") await p.F.Edit(db => db.AttachmentAvailabilities.ExecuteUpdateAsync(q => q.SetProperty(x => x.State, "MissingUnexpected")));
        }
        var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s);
        Assert.Equal(Satisfied, State(i, "FrozenMinimumSamples")); Assert.Equal(Satisfied, State(i, "FrozenOKPhotoPolicy"));
        Assert.Equal(kind == "valid" ? Satisfied : kind is "none" or "ready" or "pending" ? Missing : Invalid, State(i, "CurrentPhotoEvidence"));
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        if (kind == "valid") { var photo = Assert.Single(i.Evidence.Photos); Assert.True(photo.IsEligibleEvidence); Assert.Equal("Verified", photo.Integrity); Assert.Equal(s.A.User.Id, photo.PhotoContributorUserId); Assert.Equal(s.B.User.Id, photo.PhotoUploadedByUserId); }
        if (kind == "ready") { Assert.Equal("NotCheckedUnassociated", Assert.Single(i.Evidence.Photos).Integrity); Assert.Null(i.Evidence.Photos[0].LastReference); }
        await NoWrites(p, s);
    }
    [Fact] public async Task Missing_minimum_is_separate_from_evidence_and_never_blocks_legitimate_draft_writes() { await using var p = await PhotoFixture.Create(); await Minimum(p); }
    internal static async Task Minimum(PhotoFixture p) {
        var s = await Setup(p, minimum: "2"); var before = await SampleEvidenceTests.Facts(p); var empty = await Read(s);
        Assert.Equal(0, empty.DraftVersion); Assert.Equal(Missing, State(Item(empty, s), "FrozenMinimumSamples"));
        Assert.Equal(before, await SampleEvidenceTests.Facts(p)); Assert.Equal(0, await p.F.Read(db => db.SharedDrafts.CountAsync()));
        await DraftEvidenceAssessmentTests.Sample(s); await DraftEvidenceAssessmentTests.Photo(s);
        var ng = Item(await Read(s), s); Assert.Equal(1, ng.Evidence.ActiveSampleCount); Assert.Equal(2, ng.Evidence.MinimumSampleCount);
        Assert.Equal(Satisfied, State(ng, "CurrentPhotoEvidence")); Assert.Equal(Missing, State(ng, "FrozenMinimumSamples"));
        await DraftEvidenceAssessmentTests.Sample(s, "OK"); Assert.Equal(Satisfied, State(Item(await Read(s), s), "FrozenMinimumSamples")); await NoWrites(p, s);
    }
    [Theory][InlineData("OK")][InlineData("NG")] public async Task Genuine_bindings_cannot_prove_transition_newness_or_coverage(string to) { await using var p = await PhotoFixture.Create(); await Transition(p, to); }
    internal static async Task Transition(PhotoFixture p, string to) {
        var s = await Setup(p); var first = await DraftEvidenceAssessmentTests.Sample(s); var change = s.Mutation(first, "CorrectSample", new(to, null, null));
        var corrected = (await s.Accept(change)).Operations.Single(x => x.OperationId == change.Operations[0].OperationId);
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); var binding = new BindingSetup(s, corrected, photo, reference!);
        var accepted = (await SampleEvidenceTests.Accept(binding, SampleEvidenceTests.Input(binding, await DraftEvidenceAssessmentTests.Version(s)))).Operation;
        var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s);
        Assert.Equal(Unsupported, State(i, "TransitionPhotoNewnessAndCoverage")); Assert.Equal(Unsupported, State(i, "CurrentPhotoEvidence"));
        Assert.Equal(accepted, Assert.Single(i.Evidence.AcceptedBindings!).LastOperation); Assert.True(accepted.AssociationFactOnly); Assert.False(accepted.ProvesNewEvidence); Assert.False(accepted.ProvesCoverage);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Removed_and_all_Removed_retain_minimums_and_require_a_special_future_path(bool all) { await using var p = await PhotoFixture.Create(); await Removed(p, all); }
    internal static async Task Removed(PhotoFixture p, bool all) {
        var s = await Setup(p, minimum: "5"); await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s));
        if (all) {
            var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
            await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1, other));
        }
        var before = await SampleEvidenceTests.Facts(p); var r = await Read(s, "?pageSize=1"); var i = Item(r, s);
        Assert.Equal("Removed", i.Evidence.Disposition); Assert.Equal(5, i.Evidence.MinimumSampleCount); Assert.Equal(0, i.Evidence.ActiveSampleCount);
        Assert.Equal(NotApplicable, State(i, "FrozenMinimumSamples")); Assert.Equal(NotApplicable, State(i, "CurrentPhotoEvidence"));
        Assert.False(r.TaskObservationComplete); Assert.Equal(all, r.AllItemsRemoved);
        Assert.Equal(all, r.BlockingReasons.Contains("AllRemovedRequiresNotPerformedSubmission"));
        if (all) Assert.Contains("PqeNotPerformedConfirmationNotImplemented", r.BlockingReasons);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Unable_requires_real_reason_and_current_accepted_evidence_without_samples(bool missing) { await using var p = await PhotoFixture.Create(); await Unable(p, missing); }
    internal static async Task Unable(PhotoFixture p, bool missing) {
        var s = await Setup(p, minimum: "5"); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var u = new UnableSetup(s, photo, reference!); var original = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation;
        if (missing) File.Delete(await p.FinalPath(photo.AttachmentId));
        var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s);
        Assert.Equal("UnableToMeasureNG", i.Evidence.Disposition); Assert.Equal(original, i.Evidence.UnableOperation);
        Assert.Equal(5, i.Evidence.MinimumSampleCount); Assert.Equal(0, i.Evidence.ActiveSampleCount); Assert.Empty(i.Evidence.Samples);
        Assert.Equal(NotApplicable, State(i, "FrozenMinimumSamples")); Assert.Equal(Satisfied, State(i, "UnableToMeasureReason"));
        Assert.Equal(missing ? Invalid : Satisfied, State(i, "CurrentPhotoEvidence")); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Theory][InlineData("Required")][InlineData("Optional")][InlineData("Unrecognized text")]
    public async Task Legacy_text_never_borrows_today_policy_or_defaults_Optional(string text) { await using var p = await PhotoFixture.Create(); await Legacy(p, text); }
    internal static async Task Legacy(PhotoFixture p, string text) {
        var s = await SharedDraftFixture.Setup(p.F, photoRequirement: text, historicalPhotoPolicy: true); await DraftEvidenceAssessmentTests.Sample(s, "OK");
        var standard = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id == s.TaskId).Select(x => x.PatrolStandardId).SingleAsync());
        await PhotoPolicyFixture.ConfirmAll(p.F, standard, "Optional"); var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s);
        Assert.Equal(text, i.Evidence.PhotoPolicy.FrozenText); Assert.False(i.Evidence.PhotoPolicy.HasExplicitConfirmation);
        Assert.Equal(Unsupported, State(i, "FrozenOKPhotoPolicy")); Assert.Equal(Unsupported, State(i, "CurrentPhotoEvidence")); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Theory][InlineData("snapshot")][InlineData("attempt")][InlineData("empty")]
    public async Task Corrupt_or_empty_frozen_scope_and_missing_attempt_never_become_complete(string kind) { await using var p = await PhotoFixture.Create(); await Unavailable(p, kind); }
    internal static async Task Unavailable(PhotoFixture p, string kind) {
        var s = await Setup(p);
        if (kind == "attempt") await p.F.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync());
        else await p.F.Edit(async db => {
            var t = await db.PatrolTasks.SingleAsync();
            if (kind == "empty") { var json = System.Text.Json.Nodes.JsonNode.Parse(t.PlanningSnapshotJson!)!; json["items"] = new System.Text.Json.Nodes.JsonArray(); t.PlanningSnapshotJson = json.ToJsonString(); }
            else t.PlanningSnapshotJson = "{bad";
        });
        var before = await SampleEvidenceTests.Facts(p); var r = await Read(s); Assert.False(r.PageObservationComplete); Assert.False(r.TaskObservationComplete); Assert.Null(r.AllItemsRemoved);
        Assert.Contains("CurrentInitialAttemptUnavailable", r.BlockingReasons); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        Assert.All(r.Items, i => {
            Assert.Equal(Unsupported, State(i, "TransitionPhotoNewnessAndCoverage")); Assert.Equal(Unsupported, State(i, "UnableToMeasureReason"));
        });
        Assert.Equal(kind == "attempt" ? "ValidV1" : "Invalid", r.PlanningSnapshotStatus);
    }
    [Fact] public async Task Bounded_history_never_uses_truncated_counts_as_proof() { await using var p = await PhotoFixture.Create(); await History(p); }
    internal static async Task History(PhotoFixture p) {
        var s = await Setup(p); var previous = await DraftEvidenceAssessmentTests.Sample(s);
        for (var n = 0; n < 100; n++) {
            await using var scope = p.F.Factory.Services.CreateAsyncScope(); var batch = s.Mutation(previous, "CorrectSample", new("NG", null, null));
            previous = (await scope.ServiceProvider.GetRequiredService<SharedDraftService>().AcceptAsync(s.TaskId, batch, WorkContextFixture.Principal(s.B), default)).Operations.Single(x => x.OperationId == batch.Operations[0].OperationId);
        }
        var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s); Assert.False(i.ObservationComplete);
        Assert.Equal(Unsupported, State(i, "CurrentSampleHistory")); Assert.Equal(Unsupported, State(i, "FrozenMinimumSamples")); Assert.Null(i.Evidence.ActiveSampleCount);
        Assert.Equal(Unsupported, State(i, "TransitionPhotoNewnessAndCoverage")); Assert.Contains("TransitionHistoryUnavailable", i.BlockingReasons);
        Assert.Equal(101, await p.F.Read(db => db.ContributionOperations.CountAsync())); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Fact] public async Task Truncated_binding_observation_is_explicitly_incomplete() { await using var p = await PhotoFixture.Create(); await BindingLimit(p); }
    internal static async Task BindingLimit(PhotoFixture p) {
        var s = await Setup(p); var sample = await DraftEvidenceAssessmentTests.Sample(s); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var u = new BindingSetup(s, sample, photo, reference!);
        for (var n = 0; n < 101; n++) await SampleEvidenceTests.Direct(u, SampleEvidenceTests.Input(u, 2 + n));
        var before = await SampleEvidenceTests.Facts(p); p.Fault.ReadCount = 0; var r = await Read(s); var i = Item(r, s);
        Assert.False(i.ObservationComplete); Assert.False(r.PageObservationComplete); Assert.False(r.TaskObservationComplete);
        Assert.Equal(Unsupported, State(i, "AcceptedBindingObservation")); Assert.Equal(100, i.Evidence.AcceptedBindings!.Count); Assert.Equal(1, p.Fault.ReadCount);
        Assert.Equal(101, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync())); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Theory][InlineData("logout")][InlineData("self-password")][InlineData("reset")]
    public async Task Previously_accepted_A_provenance_remains_observable_to_current_B(string action) { await using var p = await PhotoFixture.Create(); await SourceLifecycle(p, action); }
    internal static async Task SourceLifecycle(PhotoFixture p, string action) {
        var s = await Setup(p); var capture = await DraftEvidenceAssessmentTests.Sample(s); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        else await SharedDraftFixture.Revoke(s, action);
        var before = await SampleEvidenceTests.Facts(p); var i = Item(await Read(s), s);
        Assert.Equal(Satisfied, State(i, "CurrentPhotoEvidence")); Assert.Equal(capture, Assert.Single(i.Evidence.Samples).ValueOperation);
        Assert.Equal(reference, Assert.Single(i.Evidence.Photos).LastReference); Assert.Equal(photo.ContributorUserId, i.Evidence.Photos[0].PhotoContributorUserId);
        Assert.Equal(action == "reset", await p.F.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc != null).SingleAsync()));
        Assert.Equal(before, await SampleEvidenceTests.Facts(p)); await NoWrites(p, s);
    }
}
