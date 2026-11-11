using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedPlanTests
{
    [Theory]
    [InlineData("admin", 200)][InlineData("pqe", 200)][InlineData("dev-ipqa-1", 403)]
    public async Task R03_01_Role_and_current_family_permissions_are_combined(string username, int expected)
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync("/api/line-patrol-plans/lines")).StatusCode);
        var auth = await f.Login(username); var client = f.Client(auth.AccessToken);
        Assert.Equal(expected, (int)(await client.GetAsync("/api/line-patrol-plans/lines")).StatusCode);
        if (expected != 200) return;
        var old = f.Client(f.Token(auth, claims => claims.Where(x => x.Type != PersonalAccess.FamilyClaim).ToList()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/line-patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await old.GetAsync("/api/patrol-plans")).StatusCode);
        if (username == "pqe") Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/production-lines")).StatusCode);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Username == username)).MustChangePassword = true);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/line-patrol-plans")).StatusCode);
    }
    [Fact]
    public async Task R03_02_Actual_configured_directory_and_missing_settings_leave_legacy_usable()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var client = await Admin(f, "pqe");
        var lines = (await client.GetFromJsonAsync<List<PersonalLine>>("/api/line-patrol-plans/lines"))!;
        Assert.Equal(new[] { f.Line1, f.Line2 }, lines.Select(x => x.Id));
        await f.Edit(async db => (await db.ProductionLines.SingleAsync(x => x.Id == f.Line2)).IsActive = false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/line-patrol-plans/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/patrol-plans")).StatusCode);
        await using var unconfigured = await PersonalSessionFixture.CreateAsync(environment: null);
        var auth = await unconfigured.Login("admin"); var old = unconfigured.Client(auth.AccessToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await old.GetAsync("/api/line-patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await old.GetAsync("/api/patrol-plans")).StatusCode);
    }
    [Fact]
    public async Task R03_03_04_Admitted_ownerless_create_strict_contract_and_global_number_uniqueness()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); var client = await Admin(f);
        var p = await Plan(f, s); Assert.Equal(1L, p.SharedPlanRevision); Assert.Equal("Admitted", p.ConfigurationStatus);
        Assert.Equal(0, await f.Read(db => db.PatrolPlanAssignees.CountAsync()));
        var duplicate = Input(f, s, f.Line2) with { PlanNo = p.PlanNo };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/line-patrol-plans", duplicate)).StatusCode);
        foreach (var bad in new[] { Input(f, s) with { Rounds = [] }, Input(f, s) with { Rounds = ["Day", "Day"] },
            Input(f, s) with { Rounds = ["Evening"] }, Input(f, s) with { LineId = Guid.NewGuid() },
            Input(f, s) with { PlanName = " " }, Input(f, Guid.NewGuid()) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/line-patrol-plans", bad)).StatusCode);
        var unknown = await client.PostAsJsonAsync("/api/line-patrol-plans", new { planName = "Unknown", workflowVersion = 1, assigneeUserId = TestAccounts.Ipqa1Id });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patrol-plans/{p.Id}")).StatusCode);
    }
    [Fact]
    public async Task R03_05_06_Dst_bounds_overlap_inclusive_end_and_disjoint_rounds()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); var client = await Admin(f);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s) with { EffectiveStartLocal = "2026-03-08T02:30" })).StatusCode);
        var firstInput = Input(f, s, null, "Day") with { EffectiveStartLocal = "2026-11-01T01:30", EffectiveEndLocal = "2026-11-20T07:00" };
        var response = await client.PostAsJsonAsync("/api/line-patrol-plans", firstInput); response.EnsureSuccessStatusCode();
        var first = (await response.Content.ReadFromJsonAsync<SharedPlanOutput>())!;
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0), await f.Read(db => db.PatrolPlans.Where(x => x.Id == first.Id).Select(x => x.EffectiveStartUtc).SingleAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/line-patrol-plans", firstInput with { EffectiveStartLocal = "2026-11-20T07:00", EffectiveEndLocal = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/line-patrol-plans", firstInput with { EffectiveStartLocal = "2026-11-20T07:01", EffectiveEndLocal = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s, null, "Night"))).StatusCode);
    }
    [Fact]
    public async Task R03_07_08_Cas_noop_metadata_source_changes_and_monotonic_fences()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); var p = await Plan(f, s);
        var same = await Toggle(f, p, true); Assert.Equal(p.SharedPlanRevision, same.SharedPlanRevision); Assert.Equal(p.GenerationNotBeforeUtc, same.GenerationNotBeforeUtc);
        var renamed = await Save(f, p, Edit(p) with { PlanName = "Synthetic rename" });
        Assert.Equal(2L, renamed.SharedPlanRevision); Assert.Equal(p.GenerationNotBeforeUtc, renamed.GenerationNotBeforeUtc);
        var stale = await (await Admin(f)).PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", Edit(p)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var changed = await Save(f, renamed, Edit(renamed) with { Rounds = ["Night"] });
        Assert.Equal(f.Clock.Now.UtcDateTime, changed.GenerationNotBeforeUtc);
        var disabled = await Toggle(f, changed, false); f.Clock.Now = f.Clock.Now.AddMinutes(-1);
        var reenabled = await Toggle(f, disabled, true);
        Assert.Equal(changed.GenerationNotBeforeUtc, reenabled.GenerationNotBeforeUtc);
    }
    [Fact]
    public async Task R03_10_Definition_readiness_allows_large_standard_and_preserves_raw_photo_policy()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f, 29);
        await f.Edit(async db => (await db.PatrolStandardItems.FirstAsync()).SampleCount = "51");
        var client = await Admin(f);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s))).StatusCode);
        var response = await client.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s) with { IsEnabled = false }); response.EnsureSuccessStatusCode();
        var p = (await response.Content.ReadFromJsonAsync<SharedPlanOutput>())!;
        await f.Edit(async db => (await db.PatrolStandardItems.FirstAsync()).SampleCount = "");
        await Toggle(f, p, true); f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await Generate(f));
        var task = Assert.Single(await Tasks(f)); Assert.Equal(29, task.Items.Count); Assert.All(task.Items, x => Assert.Equal("FutureRawPolicy", x.PhotoRequirement));
    }
    [Fact]
    public async Task R03_26_Unadmitted_shared_metadata_readonly_and_legacy_target_ordering()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await f.Task();
        var plan = await f.Read(db => db.PatrolTasks.Where(x => x.Id == task).Select(x => x.PatrolPlanId).SingleAsync());
        await f.Edit(async db => { var p = await db.PatrolPlans.SingleAsync(x => x.Id == plan); p.WorkflowVersion = WorkflowVersion.LineSharedV1; p.LineId = f.Line1; });
        var client = await Admin(f);
        Assert.Equal("HistoricalUnadmitted", (await client.GetFromJsonAsync<SharedPlanOutput>($"/api/line-patrol-plans/{plan}"))!.ConfigurationStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/line-patrol-plans/{plan}/disable", new SharedPlanRevisionInput(1))).StatusCode);
        var legacyTask = await f.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        var legacy = await f.Read(db => db.PatrolTasks.Where(x => x.Id == legacyTask).Select(x => x.PatrolPlanId).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/line-patrol-plans/{legacy}", Input(f, Guid.Empty))).StatusCode);
    }
}
