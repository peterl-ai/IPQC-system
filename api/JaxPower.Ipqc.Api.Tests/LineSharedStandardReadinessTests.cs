using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedStandardReadinessTests
{
    [Theory]
    [InlineData("standard-name")][InlineData("line-name")][InlineData("1001-items")]
    [InlineData("missing-items")][InlineData("null-items")][InlineData("unknown-type")]
    [InlineData("invalid-limits")][InlineData("sample-count")][InlineData("null-item")]
    public Task Direct_and_http_invalid_updates_preserve_all_shared_source_and_history(string defect) => InvalidUpdates(false, [defect]);

    internal static async Task InvalidUpdates(bool postgres, string[] defects)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var id = await Standard(f); await Plan(f, id); await Generate(f); var client = await Admin(f);
        var original = (await client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{id}"))!;
        var before = await State(f);
        foreach (var defect in defects)
        {
            var input = Input(original); var item = input.InspectionItems![0];
            input = defect switch {
                "standard-name" => input with { PatrolStandardName = "  " },
                "line-name" => input with { LineName = "  " },
                "1001-items" => input with { InspectionItems = Enumerable.Range(0, 1001).Select(_ => item with { Id = null }).ToList() },
                "missing-items" => input with { InspectionItems = [] },
                "null-items" => input with { InspectionItems = null },
                "null-item" => input with { InspectionItems = [null!] },
                "unknown-type" => input with { InspectionItems = [item with { InspectionType = "Unsupported" }] },
                "invalid-limits" => input with { InspectionItems = [item with { InspectionType = "Quantitative", LowerLimitOperator = ">=", LowerLimitValue = "bad" }] },
                _ => input with { InspectionItems = [item with { SampleCount = "51" }] }
            };
            using (var scope = f.Factory.Services.CreateScope())
            {
                var error = await Assert.ThrowsAsync<PersonalAccessException>(() => scope.ServiceProvider.GetRequiredService<PatrolStandardService>()
                    .UpdateAsync(id, input, "synthetic-direct", CancellationToken.None));
                Assert.Equal("SharedStandardNotReady", error.Message); Assert.Equal(400, error.Status);
            }
            Assert.Equal(before, await State(f));
            using var http = await client.PutAsJsonAsync($"/api/patrol-standards/{id}", input);
            Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode); Assert.Equal(before, await State(f));
        }
    }

    [Fact] public Task Successful_reorder_remove_add_and_duplicate_input_identity_keep_the_source_ready() => ValidUpdate(false);
    internal static async Task ValidUpdate(bool postgres)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var id = await Standard(f, 3); var plan = await Plan(f, id); await Generate(f); var client = await Admin(f);
        var original = (await client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{id}"))!;
        var frozen = Assert.Single(await Tasks(f)).PlanningSnapshotJson; var items = Input(original).InspectionItems!;
        var unknown = Guid.NewGuid();
        var input = Input(original) with { PatrolStandardName = "Ready reconciled source", InspectionItems =
            [items[2], items[0], items[0] with { InspectionItem = "New identity for duplicate input" }, items[1] with { Id = unknown }] };
        StandardOutput result;
        using (var scope = f.Factory.Services.CreateScope())
            result = (await scope.ServiceProvider.GetRequiredService<PatrolStandardService>().UpdateAsync(id, input, "synthetic-direct", CancellationToken.None))!;
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.InspectionItems.Select(x => x.SequenceNo));
        Assert.Equal(items[2].Id, result.InspectionItems[0].Id); Assert.Equal(items[0].Id, result.InspectionItems[1].Id);
        Assert.Equal(4, result.InspectionItems.Select(x => x.Id).Distinct().Count());
        Assert.DoesNotContain(result.InspectionItems, x => x.Id == unknown || x.Id == items[1].Id || x.Id == Guid.Empty);
        Assert.True(await f.Read(async db => LineSharedPlanService.Ready(await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems).SingleAsync())));
        using var http = await client.PutAsJsonAsync($"/api/patrol-standards/{id}", Input(result) with { PatrolStandardName = "HTTP ready source" });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        Assert.True(await f.Read(async db => LineSharedPlanService.Ready(await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems).SingleAsync())));
        var persistedPlan = await f.Read(db => db.PatrolPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id));
        Assert.True(persistedPlan.IsEnabled); Assert.Equal(id, persistedPlan.PatrolStandardId); Assert.Equal(plan.SharedPlanRevision, persistedPlan.SharedPlanRevision);
        Assert.Equal(frozen, Assert.Single(await Tasks(f)).PlanningSnapshotJson);
    }
    [Fact]
    public void Readiness_rejects_ambiguous_or_empty_persisted_item_identity_and_order()
    {
        var a = new PatrolStandardItem { Id = Guid.NewGuid(), SequenceNo = 1, InspectionType = "Qualitative" };
        var b = new PatrolStandardItem { Id = Guid.NewGuid(), SequenceNo = 2, InspectionType = "Qualitative" };
        var s = new PatrolStandard { PatrolStandardName = "Synthetic", LineName = "Synthetic", InspectionItems = [a, b] };
        Assert.True(LineSharedPlanService.Ready(s)); b.Id = a.Id; Assert.False(LineSharedPlanService.Ready(s));
        b.Id = Guid.Empty; Assert.False(LineSharedPlanService.Ready(s)); b.Id = Guid.NewGuid();
        b.SequenceNo = 1; Assert.False(LineSharedPlanService.Ready(s)); b.SequenceNo = 0; Assert.False(LineSharedPlanService.Ready(s));
    }
    [Fact]
    public async Task Shared_specific_readiness_does_not_restrict_unrelated_legacy_direct_service_behavior()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var id = await Standard(f); var client = await Admin(f);
        var source = (await client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{id}"))!;
        await f.Edit(db => {
            db.PatrolPlans.Add(new PatrolPlan { Id = Guid.NewGuid(), PlanNo = "SYN-LEGACY", PlanName = "Synthetic legacy", PatrolStandardId = id,
                IsEnabled = true, CreatedAtUtc = f.Clock.Now.UtcDateTime, UpdatedAtUtc = f.Clock.Now.UtcDateTime, EffectiveStartUtc = f.Clock.Now.UtcDateTime });
            return Task.CompletedTask;
        });
        using var scope = f.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<PatrolStandardService>().UpdateAsync(id,
            Input(source) with { PatrolStandardName = "", LineName = "", InspectionItems = [] }, "synthetic-direct", CancellationToken.None);
        Assert.NotNull(result); Assert.Empty(result.InspectionItems);
    }
    private static StandardInput Input(StandardOutput s) => new(s.PatrolStandardName, s.FactoryCode, s.FactoryName,
        s.WorkshopCode, s.LineCode, s.LineName, s.MaterialCode, s.InspectionItems.Select(x => new ItemInput(x.ProcessCode,
            x.ProcessName, x.InspectionItemCategory, x.InspectionItem, x.InspectionContent, x.UpperLimitOperator, x.UpperLimitValue,
            x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType, x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel, x.Id)).ToList());
    private static async Task<string> State(PersonalSessionFixture f) => await f.Read(async db => JsonSerializer.Serialize(new {
        Standards = await db.PatrolStandards.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Items = await db.PatrolStandardItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Plans = await db.PatrolPlans.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Tasks = await db.PatrolTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        TaskItems = await db.PatrolTaskItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Samples = await db.PatrolTaskItemSamples.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Submissions = await db.PatrolTaskSubmissions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Reviews = await db.PatrolTaskReviews.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Audit = await db.ProvisioningAuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
    }));
}
