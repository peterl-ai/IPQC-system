using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedSnapshotTests
{
    [Fact]
    public async Task R03_09_25_27_29_Frozen_bytes_identity_definitions_and_personal_read_contract()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); await Generate(f);
        var frozen = Assert.Single(await Tasks(f)); var bytes = frozen.PlanningSnapshotJson;
        var ipqa = f.Client((await f.Login()).AccessToken);
        var detail = (await ipqa.GetFromJsonAsync<LineTaskDetail>($"/api/line-patrol/tasks/{frozen.Id}"))!;
        Assert.Equal("ValidV1", detail.Summary.PlanningSnapshotStatus); Assert.Equal(1, detail.Summary.PlanningSnapshotVersion);
        Assert.Equal(1L, detail.Summary.SourcePlanRevision); Assert.Equal("Day", detail.Summary.RoundSnapshot);
        Assert.False(detail.CanExecute); Assert.False(detail.Summary.CanExecute); Assert.Equal("FoundationOnly", detail.Summary.ReadFoundationStatus);
        var original = detail.PlanningSnapshot!; Assert.Equal("FutureRawPolicy", original.Items[0].PhotoRequirement);
        var beforeAudits = await f.Read(db => db.ProvisioningAuditEvents.CountAsync());
        await Save(f, p, Edit(p) with { LineId = f.Line2, PlanName = "Future-only change", Rounds = ["Night"] });
        await f.Edit(async db => { var source = await db.PatrolStandards.Include(x => x.InspectionItems).SingleAsync();
            source.PatrolStandardName = "Later source"; source.InspectionItems[0].InspectionItem = "Later item";
            var line = await db.ProductionLines.SingleAsync(x => x.Id == f.Line1); line.Name = "Later line"; line.Revision++; });
        var after = Assert.Single(await Tasks(f)); Assert.Equal(bytes, after.PlanningSnapshotJson); Assert.Equal(f.Line1, after.LineId);
        Assert.Equal(WorkflowVersion.LineSharedV1, after.WorkflowVersion); Assert.Equal("ValidV1", PlanningSnapshot.Status(after));
        var reread = (await ipqa.GetFromJsonAsync<LineTaskDetail>($"/api/line-patrol/tasks/{frozen.Id}"))!;
        Assert.Equal(original.StandardName, reread.PlanningSnapshot!.StandardName); Assert.Equal(original.LineName, reread.PlanningSnapshot.LineName);
        Assert.Equal(beforeAudits, await f.Read(db => db.ProvisioningAuditEvents.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskItemSamples.CountAsync()));
        var admin = await Admin(f); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/line-patrol/tasks/{frozen.Id}")).StatusCode);
    }
    [Theory]
    [InlineData("json")][InlineData("line")][InlineData("plan")][InlineData("standard")][InlineData("offset")][InlineData("item")][InlineData("round")]
    public async Task R03_27_Marked_invalid_snapshot_is_listed_once_but_detail_is_unavailable(string fault)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); await Generate(f);
        await f.Edit(async db =>
        {
            var t = await db.PatrolTasks.Include(x => x.Items).SingleAsync(); var snapshot = PlanningSnapshot.Read(t)!;
            t.PlanningSnapshotJson = fault switch {
                "json" => "{bad", "line" => PlanningSnapshot.Serialize(snapshot with { LineId = f.Line2 }),
                "plan" => PlanningSnapshot.Serialize(snapshot with { PlanId = Guid.NewGuid() }),
                "standard" => PlanningSnapshot.Serialize(snapshot with { StandardId = Guid.NewGuid() }),
                "offset" => PlanningSnapshot.Serialize(snapshot with { UtcOffsetMinutes = 0 }),
                "round" => PlanningSnapshot.Serialize(snapshot with { Round = "Night" }), _ => t.PlanningSnapshotJson };
            if (fault == "item") t.Items[0].InspectionItem = "Corrupted frozen definition";
        });
        var client = f.Client((await f.Login()).AccessToken);
        var page = (await client.GetFromJsonAsync<PagedLineTasks>($"/api/line-patrol/tasks?lineId={f.Line1}"))!;
        Assert.Equal(1, page.Total); var summary = Assert.Single(page.Items); Assert.Equal("Invalid", summary.PlanningSnapshotStatus); Assert.False(summary.CanExecute);
        var unavailable = await client.GetAsync($"/api/line-patrol/tasks/{summary.Id}"); Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        Assert.Contains("TaskSnapshotUnavailable", await unavailable.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task R03_26_27_Historical_unadmitted_keeps_original_compatible_shape_and_never_infers_round_from_shift()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var id = await f.Task();
        await f.Edit(async db => (await db.PatrolTasks.SingleAsync()).Shift = "Night");
        var client = f.Client((await f.Login()).AccessToken);
        var detail = (await client.GetFromJsonAsync<LineTaskDetail>($"/api/line-patrol/tasks/{id}"))!;
        Assert.Equal("HistoricalUnadmitted", detail.Summary.PlanningSnapshotStatus); Assert.Null(detail.PlanningSnapshot);
        Assert.Null(detail.Summary.RoundSnapshot); Assert.Null(detail.Summary.SourcePlanRevision); Assert.Null(detail.Summary.ScheduledLocalDateSnapshot);
        Assert.False(detail.CanExecute); Assert.Equal("FoundationOnly", detail.Summary.ReadFoundationStatus);
    }
    [Fact]
    public async Task R03_28_New_generated_task_cannot_enter_legacy_execution_review_batch_or_reports()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); await Generate(f); var task = Assert.Single(await Tasks(f));
        var ipqa = f.Client((await f.Login()).AccessToken); var admin = await Admin(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Id}/draft", new TaskDraftInput(null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ipqa.PostAsync($"/api/patrol-tasks/{task.Id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/patrol-tasks/{task.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/patrol-tasks/batch-approve", new { taskIds = new[] { task.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/patrol-tasks/{task.Id}")).StatusCode);
        // Adversarially valid legacy completion status still cannot leak into personal-assignee reporting.
        await f.Edit(async db => (await db.PatrolTasks.SingleAsync()).Status = "Completed");
        foreach (var suffix in new[] { "", "/report", "/report.xlsx" })
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/completed-patrol-records/{task.Id}{suffix}")).StatusCode);
        Assert.Equal(task.PlanningSnapshotJson, (await Tasks(f)).Single().PlanningSnapshotJson);
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskReviews.CountAsync()));
    }
}
