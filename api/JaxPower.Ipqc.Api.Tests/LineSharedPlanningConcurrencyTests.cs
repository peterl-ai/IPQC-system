using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedPlanningConcurrencyTests
{
    [Fact] public Task R03_15_18_Concurrent_admission_and_workers() => AdmissionAndWorkers(false);
    [Fact] public Task R03_19_20_Coherent_standard_reorder_and_enable_race() => StandardRace(false);
    [Fact] public Task R03_21_Line_activity_and_atomic_audit_fences() => LineFence(false);
    [Fact] public Task R03_22_Revoked_actor_after_preflight_cannot_write() => ActorRace(false);
    [Fact] public Task R03_24_Task_graph_and_snapshot_rollback_together() => SnapshotRollback(false);

    internal static async Task AdmissionAndWorkers(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var a = await Admin(f); var b = await Admin(f, "pqe");
        var results = await Task.WhenAll(Task.Run(() => a.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s))), Task.Run(() => b.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s))));
        Assert.Equal(new[] { 201, 409 }, results.Select(x => (int)x.StatusCode).Order().ToArray());
        var outputs = await Task.WhenAll(Task.Run(() => Generate(f)), Task.Run(() => Generate(f)));
        Assert.Equal(1, outputs.Sum()); Assert.Single(await Tasks(f));
        var p = (await (await a.GetAsync("/api/line-patrol-plans")).Content.ReadFromJsonAsync<SharedPlanPage>())!.Items.Single();
        var edits = await Task.WhenAll(Task.Run(() => a.PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", Edit(p) with { PlanName = "Writer A" })),
            Task.Run(() => b.PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", Edit(p) with { PlanName = "Writer B" })));
        Assert.Equal(new[] { 200, 409 }, edits.Select(x => (int)x.StatusCode).Order().ToArray());
    }
    internal static async Task StandardRace(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); var a = await Admin(f);
        var source = (await a.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{s}"))!;
        var items = source.InspectionItems.AsEnumerable().Reverse().Select(x => new ItemInput(x.ProcessCode, x.ProcessName, x.InspectionItemCategory,
            "Edited " + x.InspectionItem, x.InspectionContent, x.UpperLimitOperator, x.UpperLimitValue, x.LowerLimitOperator,
            x.LowerLimitValue, x.InspectionType, x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel, x.Id)).ToList();
        var input = new StandardInput("Edited standard", source.FactoryCode, source.FactoryName, source.WorkshopCode,
            source.LineCode, source.LineName, source.MaterialCode, items);
        f.Fault.Arm("UPDATE \"PatrolStandardItems\"");
        var edit = Task.Run(() => a.PutAsJsonAsync($"/api/patrol-standards/{s}", input));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var generation = Task.Run(() => Generate(f)); await Task.Delay(150); Assert.False(generation.IsCompleted);
        f.Fault.Release.TrySetResult(); (await edit).EnsureSuccessStatusCode(); Assert.Equal(1, await generation);
        var frozen = PlanningSnapshot.Read(Assert.Single(await Tasks(f)))!;
        Assert.Equal("Edited standard", frozen.StandardName);
        Assert.Equal(source.InspectionItems.Select(x => x.Id).Reverse(), frozen.Items.Select(x => x.SourcePatrolStandardItemId));
        Assert.All(frozen.Items, x => Assert.StartsWith("Edited ", x.InspectionItem));
        var invalid = input with { InspectionItems = [] };
        using (var direct = f.Factory.Services.CreateScope())
        {
            var error = await Assert.ThrowsAsync<PersonalAccessException>(() => direct.ServiceProvider.GetRequiredService<PatrolStandardService>()
                .UpdateAsync(s, invalid, "synthetic", CancellationToken.None));
            Assert.Equal(400, error.Status);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync($"/api/patrol-standards/{s}", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.DeleteAsync($"/api/patrol-standards/{s}")).StatusCode);
        // Reverse order: Standard becomes incomplete while disabled; racing enable cannot admit it.
        p = await Toggle(f, p, false);
        f.Fault.Arm("DELETE FROM \"PatrolStandardItems\"");
        var incomplete = Task.Run(() => a.PutAsJsonAsync($"/api/patrol-standards/{s}", invalid));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var enable = Task.Run(() => a.PostAsJsonAsync($"/api/line-patrol-plans/{p.Id}/enable", new SharedPlanRevisionInput(p.SharedPlanRevision!.Value)));
        await Task.Delay(150); f.Fault.Release.TrySetResult(); (await incomplete).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await enable).StatusCode);
    }
    internal static async Task LineFence(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); var a = await Admin(f);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        f.Fault.Arm("INSERT INTO \"ProvisioningAuditEvents\"", failure: true);
        var failed = await a.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate", new ProvisioningRevisionInput(1));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(await f.Read(db => db.ProductionLines.Where(x => x.Id == f.Line1).Select(x => x.IsActive).SingleAsync()));
        Assert.Equal(1L, await f.Read(db => db.PatrolPlans.Where(x => x.Id == p.Id).Select(x => x.SharedPlanRevision).SingleAsync()));
        Assert.Equal(0, await f.Read(db => db.ProvisioningAuditEvents.CountAsync()));
        f.Fault.Arm("INSERT INTO \"ProvisioningAuditEvents\"");
        var deactivate = Task.Run(() => a.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate", new ProvisioningRevisionInput(1)));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var generation = Task.Run(() => Generate(f)); await Task.Delay(150); Assert.False(generation.IsCompleted);
        f.Fault.Release.TrySetResult(); (await deactivate).EnsureSuccessStatusCode(); Assert.Equal(0, await generation);
        f.Clock.Now = f.Clock.Now.AddDays(1); a = await Admin(f);
        (await a.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/activate", new ProvisioningRevisionInput(2))).EnsureSuccessStatusCode();
        Assert.Equal(0, await Generate(f));
        var current = (await a.GetFromJsonAsync<SharedPlanOutput>($"/api/line-patrol-plans/{p.Id}"))!;
        Assert.Equal(3L, current.SharedPlanRevision); Assert.Equal(f.Clock.Now.UtcDateTime, current.GenerationNotBeforeUtc);
        (await a.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/activate", new ProvisioningRevisionInput(3))).EnsureSuccessStatusCode();
        Assert.Equal(3L, (await a.GetFromJsonAsync<SharedPlanOutput>($"/api/line-patrol-plans/{p.Id}"))!.SharedPlanRevision);
    }
    internal static async Task ActorRace(bool postgres, string closure = "AuthVersion")
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); var s = await Standard(f); var client = await Admin(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoke = Task.Run(async () =>
        {
            using var scope = f.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            await scope.ServiceProvider.GetRequiredService<PersonalSessionStore>().WriteAsync(TestAccounts.AdminId, async () =>
            {
                var user = await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.AdminId);
                switch (closure)
                {
                    case "Logout":
                        foreach (var token in await db.RefreshTokenSessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ToListAsync())
                            token.RevokedAtUtc = f.Clock.Now.UtcDateTime;
                        break;
                    case "Role": user.Role = "IPQA"; user.AuthVersion++; break;
                    case "Deactivation": user.IsActive = false; user.AuthVersion++; break;
                    default: user.AuthVersion++; break;
                }
                await db.SaveChangesAsync(); entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true;
            }, CancellationToken.None);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var request = Task.Run(() => client.PostAsJsonAsync("/api/line-patrol-plans", Input(f, s)));
        await Task.Delay(150); Assert.False(request.IsCompleted); release.TrySetResult(); await revoke;
        Assert.Equal(HttpStatusCode.Unauthorized, (await request).StatusCode); Assert.Equal(0, await f.Read(db => db.PatrolPlans.CountAsync()));
    }
    internal static async Task SnapshotRollback(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s);
        f.Fault.Arm("INSERT INTO \"PatrolTaskItems\"", failure: true);
        Assert.Equal(0, await Generate(f)); Assert.Empty(await Tasks(f)); Assert.Equal(0, await f.Read(db => db.PatrolTaskItems.CountAsync()));
        Assert.Equal(1, await Generate(f)); Assert.Equal("ValidV1", PlanningSnapshot.Status(Assert.Single(await Tasks(f))));
    }
    [Fact] public Task R03_17_18_Discovery_facts_are_reloaded_after_committed_configuration_change() => StaleDiscovery(false);
    [Fact] public Task R03_23_Same_actor_line_and_plan_writes_do_not_deadlock_audit() => SameActorLineAndPlan(false);
    [Fact] public Task R03_19_24_Standard_two_save_and_task_commit_failures_are_atomic() => CommitRollback(false);
    [Fact]
    public async Task R03_15_Transient_retries_are_bounded_and_do_not_claim_success()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); var p = await Plan(f, s);
        var a = await Admin(f); f.Fault.ArmBusy(3, "UPDATE \"PatrolPlans\"");
        var r = await a.PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", Edit(p) with { PlanName = "Never committed" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(p.PlanName, (await a.GetFromJsonAsync<SharedPlanOutput>($"/api/line-patrol-plans/{p.Id}"))!.PlanName);
    }
    internal static async Task StaleDiscovery(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); var a = await Admin(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.CommitFault.AfterCommit = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); };
        var generation = Task.Run(() => Generate(f)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // The first transaction has committed and released its locks; the poll's candidate list is now stale.
        var changed = await Save(f, p, Edit(p) with { LineId = f.Line2, Rounds = ["Night"], PlanName = "Future facts" });
        release.TrySetResult(); Assert.Equal(0, await generation); Assert.Empty(await Tasks(f));
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await Generate(f));
        var task = Assert.Single(await Tasks(f)); Assert.Equal(f.Line2, task.LineId); Assert.Equal(changed.SharedPlanRevision, PlanningSnapshot.Read(task)!.SourcePlanRevision);
        Assert.Equal("Night", PlanningSnapshot.Read(task)!.Round);
        // Opposite order: insertion holds the gate; disable must wait, preserving a wholly committed old snapshot.
        f.Clock.Now = f.Clock.Now.AddDays(1); a = await Admin(f); f.Fault.Arm("UPDATE \"PatrolPlans\"");
        var oldWins = Task.Run(() => Generate(f)); await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var disable = Task.Run(() => a.PostAsJsonAsync($"/api/line-patrol-plans/{p.Id}/disable", new SharedPlanRevisionInput(changed.SharedPlanRevision!.Value)));
        await Task.Delay(150); Assert.False(disable.IsCompleted); f.Fault.Release.TrySetResult();
        Assert.Equal(1, await oldWins); (await disable).EnsureSuccessStatusCode(); Assert.Equal(0, await Generate(f));
        Assert.All(await Tasks(f), x => Assert.Equal("ValidV1", PlanningSnapshot.Status(x)));
    }
    internal static async Task SameActorLineAndPlan(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); var s = await Standard(f); var p = await Plan(f, s); var a = await Admin(f);
        f.Fault.Arm("INSERT INTO \"ProvisioningAuditEvents\"");
        var rename = Task.Run(() => a.PutAsJsonAsync($"/api/admin/production-lines/{f.Line1}", new LineRenameInput("Synthetic renamed line", 1)));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var plan = Task.Run(() => a.PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", Edit(p) with { PlanName = "Synthetic renamed plan" }));
        await Task.Delay(150); Assert.False(plan.IsCompleted);
        f.Fault.Release.TrySetResult(); (await rename).EnsureSuccessStatusCode(); (await plan).EnsureSuccessStatusCode();
        Assert.Equal(1, await f.Read(db => db.ProvisioningAuditEvents.CountAsync()));
        Assert.Equal(2L, (await a.GetFromJsonAsync<SharedPlanOutput>($"/api/line-patrol-plans/{p.Id}"))!.SharedPlanRevision);
    }
    internal static async Task CommitRollback(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); var a = await Admin(f);
        f.CommitFault.FailTaskCommit = true; Assert.Equal(0, await Generate(f)); Assert.Empty(await Tasks(f));
        Assert.Equal(0, await f.Read(db => db.PatrolTaskItems.CountAsync()));
        var standard = (await a.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{s}"))!;
        var input = new StandardInput("Uncommitted header", "", "", "", "", "Synthetic", "", standard.InspectionItems.AsEnumerable().Reverse()
            .Select(x => new ItemInput("", "", "", "Uncommitted item", "", "", "", "", "", "Qualitative", "", "", "Raw", "", x.Id)).ToList());
        f.CommitFault.Armed = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await a.PutAsJsonAsync($"/api/patrol-standards/{s}", input)).StatusCode);
        var after = (await a.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{s}"))!;
        Assert.Equal(standard.PatrolStandardName, after.PatrolStandardName);
        Assert.Equal(standard.InspectionItems, after.InspectionItems);
        Assert.Equal(1, await Generate(f)); Assert.Equal("ValidV1", PlanningSnapshot.Status(Assert.Single(await Tasks(f))));
    }
    [Theory]
    [InlineData("Logout")][InlineData("Role")][InlineData("Deactivation")]
    public Task R03_22_Current_family_recheck_rejects_each_security_closure(string closure) => ActorRace(false, closure);
}
