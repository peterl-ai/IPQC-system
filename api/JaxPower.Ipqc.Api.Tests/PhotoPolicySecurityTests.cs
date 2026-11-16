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

public sealed class PhotoPolicySecurityTests
{
    [Theory][InlineData("required")][InlineData("Unknown")][InlineData("")][InlineData(" Required ")]
    public async Task Values_are_explicit_not_inferred(string value) { await using var f = await PersonalSessionFixture.CreateAsync(); await Value(f, value); }
    internal static async Task Value(PersonalSessionFixture f, string value) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0];
        var before = await PhotoPolicyTests.Facts(f);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), PhotoPolicyFixture.Input(item, value))).StatusCode);
        Assert.Equal(before, await PhotoPolicyTests.Facts(f));
    }
    [Fact] public async Task Claims_do_not_manufacture_signatures_and_current_family_is_mandatory() { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }
    internal static async Task Authority(PersonalSessionFixture f) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var admin = await f.Login("admin"); var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0];
        var input = PhotoPolicyFixture.Input(item); var url = PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId); var before = await PhotoPolicyTests.Facts(f);
        var forged = f.Token(admin, cs => { cs.Add(new Claim(IpqcPermissions.ClaimType, IpqcPermissions.PhotoPoliciesConfirm)); return cs; });
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client(forged).PostAsJsonAsync(url, input)).StatusCode);
        var noFamily = f.Token(pqe, cs => cs.Where(x => x.Type != "personal_session_id").ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(noFamily).PostAsJsonAsync(url, input)).StatusCode);
        await using var scope = f.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<PhotoPolicyService>();
        Assert.Equal(403, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.ConfirmAsync(s, item.StandardItemId, input, WorkContextFixture.Principal(admin, "PQE"), default))).Status);
        var json = JsonSerializer.Serialize(input, DraftCanonical.Json);
        foreach (var field in new[] { "\"confirmationId\":\"" + Guid.NewGuid() + "\"", "\"confirmedByUserId\":\"" + pqe.User.Id + "\"", "\"value\":\"Required\"" }) {
            var response = await f.Client(pqe.AccessToken).PostAsync(url, new StringContent(json[..^1] + "," + field + "}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(before, await PhotoPolicyTests.Facts(f));
        (await f.Client(pqe.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(pqe.RefreshToken))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(pqe.AccessToken).PostAsJsonAsync(url, input)).StatusCode);
        Assert.Equal(before, await PhotoPolicyTests.Facts(f));
    }
    [Fact] public async Task Wrong_item_definition_scope_and_revision_are_not_approval() { await using var f = await PersonalSessionFixture.CreateAsync(); await Scope(f); }
    internal static async Task Scope(PersonalSessionFixture f) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var other = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe");
        var page = await PhotoPolicyFixture.Read(f, s, pqe); var i = page.Items[0]; var b = PhotoPolicyFixture.Input(i);
        foreach (var input in new[] { b with { ExpectedDefinitionHash = new string('A', 64) }, b with { ExpectedDefinitionId = Guid.NewGuid() }, b with { ExpectedConfirmationId = Guid.NewGuid() } })
            Assert.Equal(HttpStatusCode.Conflict, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, i.StandardItemId), input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(other, i.StandardItemId), b)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, page.Items[1].StandardItemId), b)).StatusCode);
        Assert.Equal(0, await f.Read(db => db.PhotoPolicyDefinitions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
    }
    [Fact] public async Task Confirmed_Optional_never_supplies_missing_samples_or_waives_NG_transition_evidence() { await using var p = await PhotoFixture.Create(); await Minimum(p); }
    internal static async Task Minimum(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F, sampleCount: "5"); await DraftEvidenceAssessmentTests.Sample(s, "OK"); var i = await DraftEvidenceAssessmentTests.Item(s);
        Assert.True(i.PhotoPolicy.HasExplicitConfirmation); Assert.Equal("Optional", i.PhotoPolicy.RecognizedText); Assert.False(i.MeetsMinimum); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus);
        var ng = await DraftEvidenceAssessmentTests.Sample(s); Assert.Equal("MissingEvidence", (await DraftEvidenceAssessmentTests.Item(s)).EvidenceStatus);
        var change = s.Mutation(ng, "CorrectSample", new("OK", null, null)); await s.Accept(change); await DraftEvidenceAssessmentTests.Photo(s);
        i = await DraftEvidenceAssessmentTests.Item(s); Assert.Equal("UnsupportedUntilFutureWorkflow", i.EvidenceStatus); Assert.Contains("NewEvidenceForEachRelevantTransition", i.UnprovenFacts);
    }
    [Theory][InlineData("INSERT INTO \"PhotoPolicyDefinitions\"")][InlineData("INSERT INTO \"PhotoPolicyStates\"")]
    [InlineData("INSERT INTO \"PhotoPolicyConfirmations\"")][InlineData("UPDATE \"PhotoPolicyStates\"")][InlineData("commit")]
    public async Task Confirmation_rollback_never_leaves_a_fake_approval(string point) { await using var f = await PersonalSessionFixture.CreateAsync(); await Rollback(f, point); }
    internal static async Task Rollback(PersonalSessionFixture f, string point) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var b = PhotoPolicyFixture.Input(item);
        var before = await PhotoPolicyTests.Facts(f); if (point == "commit") f.CommitFault.Armed = true; else f.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), b)).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await PhotoPolicyTests.Facts(f)); await PhotoPolicyFixture.Confirm(f, s, item.StandardItemId, b, pqe);
    }
    [Theory][InlineData("INSERT INTO \"PatrolTasks\"")][InlineData("INSERT INTO \"PatrolTaskItems\"")]
    [InlineData("INSERT INTO \"FrozenTaskPhotoPolicies\"")][InlineData("commit")]
    public async Task Task_graph_snapshot_identities_and_policies_roll_back_together(string point) { await using var f = await PersonalSessionFixture.CreateAsync(); await GenerationRollback(f, point); }
    internal static async Task GenerationRollback(PersonalSessionFixture f, string point) {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); var s = await LineSharedPlanningFixture.Standard(f); await LineSharedPlanningFixture.Plan(f, s); f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var before = await PhotoPolicyTests.Facts(f); if (point == "commit") f.CommitFault.FailTaskCommit = true; else f.Fault.Arm(point, true);
        Assert.Equal(0, await PhotoPolicyFixture.GenerateStrict(f)); if (point != "commit") Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(0, await f.Read(db => db.PatrolTasks.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskItems.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync())); Assert.Equal(before, await PhotoPolicyTests.Facts(f)); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f));
    }
}
