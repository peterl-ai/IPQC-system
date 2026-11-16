using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SubmitPreflightSecurityTests
{
    [Fact] public async Task HTTP_and_direct_reads_require_current_personal_IPQA_authority() { await using var p = await PhotoFixture.Create(); await Authority(p); }
    internal static async Task Authority(PhotoFixture p) {
        var f = p.F; var s = await SubmitPreflightTests.Setup(p); var url = SubmitPreflightTests.Url(s);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(url + "?page=bad")).StatusCode);
        foreach (var role in new[] { "admin", "pqe" }) Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(await f.Login(role)).GetAsync(url)).StatusCode);
        var noFamily = f.Token(s.B, cs => cs.Where(x => x.Type != PersonalAccess.FamilyClaim).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(noFamily).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(s.Source.ProofSecret).GetAsync(url)).StatusCode);
        await using (var scope = f.Factory.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
            var claimsOnly = new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims));
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.SubmitPreflightAsync(s.TaskId, claimsOnly, default))).Status);
            var familyless = new ClaimsPrincipal(new ClaimsIdentity(claimsOnly.Claims.Where(x => x.Type != PersonalAccess.FamilyClaim), "synthetic"));
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.SubmitPreflightAsync(s.TaskId, familyless, default))).Status);
        }
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client().GetAsync(url)).StatusCode);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = false);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        var before = await SampleEvidenceTests.Facts(p);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(url)).StatusCode);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Fact] public async Task Real_task_line_item_and_attempt_assertions_cannot_be_substituted() { await using var p = await PhotoFixture.Create(); await Scope(p); }
    internal static async Task Scope(PhotoFixture p) {
        var f = p.F; var s = await SubmitPreflightTests.Setup(p); var url = SubmitPreflightTests.Url(s);
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        var before = await SampleEvidenceTests.Facts(p);
        var exact = await SubmitPreflightTests.Read(s, $"?expectedTaskLineId={f.Line1}&expectedTaskItemId={s.ItemId}&expectedInspectionAttemptId={s.AttemptId}");
        Assert.Equal(s.AttemptId, SubmitPreflightTests.Item(exact, s).Evidence.InspectionAttemptId);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(url + $"?expectedTaskLineId={f.Line2}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(url + $"?expectedTaskItemId={s.ItemId}&expectedInspectionAttemptId={other.InspectionAttemptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(SubmitPreflightTests.Url(s with { TaskId = Guid.NewGuid() }))).StatusCode);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(f));
        var task = await f.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId).Select(x => x.Id).SingleAsync());
        s = s with { A = await f.Login(), B = await f.Login("dev-ipqa-2") };
        var foreign = (await s.Client().GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!.Items[0];
        before = await SampleEvidenceTests.Facts(p);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(url + $"?expectedTaskItemId={foreign.TaskItemId}&expectedInspectionAttemptId={foreign.InspectionAttemptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(url + $"?expectedTaskItemId={s.ItemId}&expectedInspectionAttemptId={foreign.InspectionAttemptId}")).StatusCode);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Fact] public async Task Pagination_bounds_and_stale_versions_never_claim_whole_task_completeness() { await using var p = await PhotoFixture.Create(); await Bounds(p); }
    internal static async Task Bounds(PhotoFixture p) {
        var s = await SubmitPreflightTests.Setup(p); var before = await SampleEvidenceTests.Facts(p);
        var one = await SubmitPreflightTests.Read(s, "?pageSize=1&expectedDraftVersion=0");
        var two = await SubmitPreflightTests.Read(s, "?page=2&pageSize=1&expectedDraftVersion=0");
        Assert.NotEqual(Assert.Single(one.Items).Evidence.TaskItemId, Assert.Single(two.Items).Evidence.TaskItemId);
        Assert.False(one.TaskObservationComplete); Assert.False(two.TaskObservationComplete);
        var empty = await SubmitPreflightTests.Read(s, "?page=3&pageSize=1"); Assert.Empty(empty.Items); Assert.False(empty.PageObservationComplete); Assert.False(empty.TaskObservationComplete); Assert.False(empty.AllItemsRemoved);
        foreach (var query in new[] { "?page=0", "?pageSize=0", "?pageSize=21", "?page=2147483647&pageSize=20", "?expectedDraftVersion=-1", $"?expectedTaskLineId={Guid.Empty}", $"?expectedTaskItemId={s.ItemId}", $"?expectedInspectionAttemptId={s.AttemptId}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(SubmitPreflightTests.Url(s) + query)).StatusCode);
        Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        await DraftEvidenceAssessmentTests.Sample(s, "OK");
        var stale = await s.Client().GetAsync(SubmitPreflightTests.Url(s) + "?expectedDraftVersion=0"); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("SubmitPreflightVersionConflict", await stale.Content.ReadAsStringAsync());
        Assert.Equal(1, (await SubmitPreflightTests.Read(s, "?expectedDraftVersion=1")).DraftVersion); await SubmitPreflightTests.NoWrites(p, s);
    }
}
