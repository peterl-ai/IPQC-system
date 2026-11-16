using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPolicyTests
{
    [Fact] public async Task Only_current_PQE_can_explicitly_confirm_and_new_tasks_freeze_the_exact_receipt() { await using var f = await PersonalSessionFixture.CreateAsync(); await Lifecycle(f); }
    internal static async Task Lifecycle(PersonalSessionFixture f) {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false);
        var pqe = await f.Login("pqe"); var admin = await f.Login("admin"); var ipqa = await f.Login();
        var page = await PhotoPolicyFixture.Read(f, s, pqe); var item = page.Items[0]; Assert.Null(item.DefinitionId); Assert.Null(item.DefinitionVersion); Assert.Null(item.Confirmation);
        Assert.Equal(0, await f.Read(db => db.PhotoPolicyDefinitions.CountAsync())); // GET must not manufacture revision/confirmation facts.
        var input = PhotoPolicyFixture.Input(item, "Required");
        foreach (var actor in new[] { admin, ipqa }) Assert.Equal(HttpStatusCode.Forbidden,
            (await f.Client(actor.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), input)).StatusCode);
        var r = await PhotoPolicyFixture.Confirm(f, s, item.StandardItemId, input, pqe);
        Assert.NotEqual(input.RequestId, r.ConfirmationId); Assert.Equal(pqe.User.Id, r.ConfirmedByUserId); Assert.Equal(pqe.PersonalSessionId, r.ConfirmedPersonalSessionId);
        Assert.Equal(1, r.DefinitionVersion); Assert.Equal(1, r.PolicyVersion); Assert.Equal("Required", r.Value);
        Assert.Equal(r, await PhotoPolicyFixture.Confirm(f, s, item.StandardItemId, input, pqe));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), input with { Value = "Optional" })).StatusCode);
        await PhotoPolicyFixture.ConfirmAll(f, s); await LineSharedPlanningFixture.Plan(f, s);
        f.Clock.Now = f.Clock.Now.AddMinutes(1); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f)); Assert.Equal(0, await PhotoPolicyFixture.GenerateStrict(f));
        Assert.Equal(2, await f.Read(db => db.FrozenTaskPhotoPolicies.CountAsync()));
        var task = Assert.Single(await LineSharedPlanningFixture.Tasks(f));
        var frozen = (await f.Client(ipqa.AccessToken).GetFromJsonAsync<TaskPhotoPolicies>($"/api/line-patrol/tasks/{task.Id}/photo-policies"))!;
        Assert.False(frozen.CanExecute); Assert.Equal(r, frozen.Items.Single(x => x.FrozenPolicy!.Policy.StandardItemId == item.StandardItemId).FrozenPolicy!.Policy);
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync()));
    }
    internal static StandardInput Edit(StandardOutput s) => new(s.PatrolStandardName, s.FactoryCode, s.FactoryName, s.WorkshopCode, s.LineCode, s.LineName, s.MaterialCode,
        s.InspectionItems.Select(x => new ItemInput(x.ProcessCode, x.ProcessName, x.InspectionItemCategory, x.InspectionItem, x.InspectionContent,
            x.UpperLimitOperator, x.UpperLimitValue, x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType, x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel, x.Id)).ToList());
    internal static async Task<string> Facts(PersonalSessionFixture f) => await f.Read(async db => JsonSerializer.Serialize(new {
        Definitions = await db.PhotoPolicyDefinitions.OrderBy(x => x.Id).ToListAsync(), Confirmations = await db.PhotoPolicyConfirmations.OrderBy(x => x.Id).ToListAsync(),
        States = await db.PhotoPolicyStates.OrderBy(x => x.StandardItemId).ToListAsync(), Frozen = await db.FrozenTaskPhotoPolicies.OrderBy(x => x.TaskItemId).ToListAsync()
    }));
    [Fact] public async Task Definition_drift_noop_reversion_copy_and_historical_freeze_are_explicit() { await using var f = await PersonalSessionFixture.CreateAsync(); await Drift(f); }
    internal static async Task Drift(PersonalSessionFixture f) {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); var s = await LineSharedPlanningFixture.Standard(f); var admin = await f.Login("admin"); var pqe = await f.Login("pqe");
        await LineSharedPlanningFixture.Plan(f, s); f.Clock.Now = f.Clock.Now.AddMinutes(1); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f));
        var task = Assert.Single(await LineSharedPlanningFixture.Tasks(f)); var oldFrozen = await f.Read(db => db.FrozenTaskPhotoPolicies.OrderBy(x => x.TaskItemId).Select(x => x.SnapshotJson).ToArrayAsync());
        var original = (await f.Client(admin.AccessToken).GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{s}"))!; var originalInput = Edit(original);
        var originalPolicies = await PhotoPolicyFixture.Read(f, s, pqe); var old = originalPolicies.Items[0];
        var unchangedOther = originalPolicies.Items[1].Confirmation;
        var noOp = await f.Client(admin.AccessToken).PutAsJsonAsync($"/api/patrol-standards/{s}", originalInput); noOp.EnsureSuccessStatusCode();
        Assert.Equal(old.Confirmation, (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0].Confirmation);
        var edited = originalInput with { InspectionItems = originalInput.InspectionItems!.Select((x, i) => i == 0 ? x with { InspectionContent = "Changed actual definition" } : x).ToList() };
        (await f.Client(admin.AccessToken).PutAsJsonAsync($"/api/patrol-standards/{s}", edited)).EnsureSuccessStatusCode();
        var current = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; Assert.Null(current.Confirmation); Assert.NotEqual(old.DefinitionId, current.DefinitionId);
        Assert.NotNull(unchangedOther); Assert.Equal(unchangedOther, (await PhotoPolicyFixture.Read(f, s, pqe)).Items[1].Confirmation);
        (await f.Client(admin.AccessToken).PutAsJsonAsync($"/api/patrol-standards/{s}", originalInput)).EnsureSuccessStatusCode();
        current = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; Assert.Equal(old.DefinitionHash, current.DefinitionHash); Assert.NotEqual(old.DefinitionId, current.DefinitionId); Assert.Null(current.Confirmation);
        var stale = PhotoPolicyFixture.Input(old); Assert.Equal(HttpStatusCode.Conflict, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, old.StandardItemId), stale)).StatusCode);
        var fixedPolicy = await PhotoPolicyFixture.Confirm(f, s, current.StandardItemId, PhotoPolicyFixture.Input(current, "Required"), pqe); Assert.True(fixedPolicy.DefinitionVersion > old.Confirmation!.DefinitionVersion);
        Assert.Equal(oldFrozen, await f.Read(db => db.FrozenTaskPhotoPolicies.OrderBy(x => x.TaskItemId).Select(x => x.SnapshotJson).ToArrayAsync()));
        await using var scope = f.Factory.Services.CreateAsyncScope(); var frozen = await scope.ServiceProvider.GetRequiredService<PhotoPolicyService>().FrozenAsync(task, task.Items[0], default);
        Assert.Equal("ExplicitlyConfirmed", frozen.State); Assert.NotEqual(fixedPolicy.ConfirmationId, frozen.FrozenPolicy!.Policy.ConfirmationId);
        var copyResponse = await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/patrol-standards/{s}/copy", new CopyInput("Synthetic unconfirmed copy")); copyResponse.EnsureSuccessStatusCode();
        var copy = (await copyResponse.Content.ReadFromJsonAsync<StandardOutput>())!; Assert.All((await PhotoPolicyFixture.Read(f, copy.Id, pqe)).Items, x => Assert.Null(x.Confirmation));
    }
    [Theory][InlineData("Required", "none")][InlineData("Required", "unassociated")][InlineData("Required", "valid")]
    [InlineData("Required", "missing")][InlineData("Required", "corrupt")][InlineData("Required", "detached")][InlineData("Optional", "none")]
    public async Task Ordinary_OK_uses_only_real_confirmed_frozen_policy(string value, string photo) { await using var p = await PhotoFixture.Create(); await Evidence(p, value, photo); }
    internal static async Task Evidence(PhotoFixture p, string value, string photo) {
        var s = await SharedDraftFixture.Setup(p.F); // First genuinely generated task freezes Optional; use the next occurrence for the new explicit Required decision.
        var standard = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id == s.TaskId).Select(x => x.PatrolStandardId).SingleAsync()); var pqe = await p.F.Login("pqe");
        var page = await PhotoPolicyFixture.Read(p.F, standard, pqe); var sourceItem = await p.F.Read(db => db.PatrolTaskItems.Where(x => x.Id == s.ItemId).Select(x => x.SourcePatrolStandardItemId).SingleAsync());
        var item = page.Items.Single(x => x.StandardItemId == sourceItem);
        await PhotoPolicyFixture.Confirm(p.F, standard, sourceItem, PhotoPolicyFixture.Input(item, value), pqe);
        p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(p.F));
        var task = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync());
        var a = await p.F.Login(); var b = await p.F.Login("dev-ipqa-2"); var scope = (await p.F.Client(b.AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!;
        var targetId = await p.F.Read(db => db.PatrolTaskItems.Where(x => x.PatrolTaskId == task && x.SourcePatrolStandardItemId == sourceItem).Select(x => x.Id).SingleAsync());
        var target = scope.Items.Single(x => x.TaskItemId == targetId);
        s = s with { TaskId = task, ItemId = target.TaskItemId, AttemptId = target.InspectionAttemptId, A = a, B = b, Source = await WorkContextFixture.Issue(p.F, task, a) };
        await DraftEvidenceAssessmentTests.Sample(s, "OK");
        if (photo != "none") {
            var (obj, reference) = await DraftEvidenceAssessmentTests.Photo(s, associate: photo != "unassociated");
            if (photo == "missing") File.Delete(await p.FinalPath(obj.AttachmentId));
            if (photo == "corrupt") await File.WriteAllBytesAsync(await p.FinalPath(obj.AttachmentId), [1, 2, 3]);
            if (photo == "detached") await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, obj, 2, previous: reference, kind: "DetachAttachment"));
        }
        var before = await Facts(p.F); var result = await DraftEvidenceAssessmentTests.Item(s);
        Assert.True(result.PhotoPolicy.HasExplicitConfirmation); Assert.Equal(value, result.PhotoPolicy.RecognizedText); Assert.NotNull(result.PhotoPolicy.FrozenPolicy);
        Assert.Equal(value == "Optional" || photo == "valid" ? "Satisfied" : photo is "none" or "unassociated" ? "MissingEvidence" : "InvalidEvidence", result.EvidenceStatus);
        Assert.Equal(before, await Facts(p.F)); Assert.Equal("NotEvaluatedForFormalSubmission", (await DraftEvidenceAssessmentTests.Read(s)).Completeness);
    }
    [Fact] public async Task Unconfirmed_admission_and_existing_Task_never_borrow_a_later_confirmation() { await using var p = await PhotoFixture.Create(); await Legacy(p); }
    internal static async Task Legacy(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F, photoRequirement: "Optional", historicalPhotoPolicy: true);
        var standard = await p.F.Read(db => db.PatrolTasks.Where(x => x.Id == s.TaskId).Select(x => x.PatrolStandardId).SingleAsync());
        await DraftEvidenceAssessmentTests.Sample(s, "OK"); Assert.Equal("PolicyConfigurationUnresolved", (await DraftEvidenceAssessmentTests.Item(s)).EvidenceStatus);
        p.F.Clock.Now = p.F.Clock.Now.AddHours(12); Assert.Equal(0, await PhotoPolicyFixture.GenerateStrict(p.F));
        await PhotoPolicyFixture.ConfirmAll(p.F, standard); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(p.F));
        s = s with { B = await p.F.Login("dev-ipqa-2") };
        Assert.False((await DraftEvidenceAssessmentTests.Item(s)).PhotoPolicy.HasExplicitConfirmation);
        Assert.Equal(2, await p.F.Read(db => db.FrozenTaskPhotoPolicies.CountAsync())); Assert.Equal(2, await p.F.Read(db => db.PatrolTasks.CountAsync()));
    }
}
