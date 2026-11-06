using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkflowLineFoundationTests
{
    [Theory]
    [InlineData(WorkflowVersion.LegacyPersonalV1, "draft")]
    [InlineData(WorkflowVersion.LegacyPersonalV1, "submit")]
    [InlineData(WorkflowVersion.LineSharedV1, "draft")]
    [InlineData(WorkflowVersion.LineSharedV1, "submit")]
    public async Task Execution_ownership_precedes_workflow_classification(WorkflowVersion version, string action)
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(version);
        using var other = await TestAccounts.ClientAsync(app.Factory, "dev-ipqa-2");
        var before = await app.StateAsync();
        var denied = action == "draft"
            ? await other.PutAsJsonAsync($"/api/patrol-tasks/{id.Task}/draft", new TaskDraftInput("Day", []))
            : await other.PostAsync($"/api/patrol-tasks/{id.Task}/submit", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, await app.StateAsync());

        var owned = await app.MutateTaskAsync(action, id.Task, id.Item, id.Task);
        Assert.Equal(version == WorkflowVersion.LegacyPersonalV1 ? HttpStatusCode.OK : HttpStatusCode.Conflict, owned.StatusCode);
        if (version == WorkflowVersion.LineSharedV1) Assert.Equal(before, await app.StateAsync());
        else
        {
            var detail = (await app.Admin.GetFromJsonAsync<TaskDetail>($"/api/patrol-tasks/{id.Task}"))!;
            Assert.Equal(action == "draft" ? "InProgress" : "PendingApproval", detail.Summary.Status);
        }
    }

    [Fact]
    public async Task Legacy_plan_and_task_reads_isolate_shared_records_before_pagination_and_projection()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var legacy = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        var completed = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed");
        var shared = await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var sharedCompleted = await app.SeedAsync(WorkflowVersion.LineSharedV1, "Completed");
        var before = await app.StateAsync();
        using var pqe = await TestAccounts.ClientAsync(app.Factory, "pqe");
        foreach (var client in new[] { app.Admin, pqe })
        {
            var plans = new List<Guid>(); var tasks = new List<Guid>();
            foreach (var page in new[] { 1, 2 })
            {
                var planPage = (await client.GetFromJsonAsync<PagedPlans>($"/api/patrol-plans?pageSize=1&page={page}"))!;
                Assert.Equal(2, planPage.Total); plans.Add(Assert.Single(planPage.Items).Id);
                var taskPage = (await client.GetFromJsonAsync<PagedTasks>($"/api/patrol-tasks?pageSize=1&page={page}"))!;
                Assert.Equal(2, taskPage.Total); tasks.Add(Assert.Single(taskPage.Items).Id);
            }
            Assert.Equal(new[] { legacy.Plan, completed.Plan }.Order(), plans.Order());
            Assert.Equal(new[] { legacy.Task, completed.Task }.Order(), tasks.Order());
            foreach (var target in new[] { shared, sharedCompleted })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patrol-plans/{target.Plan}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patrol-tasks/{target.Task}")).StatusCode);
                var filtered = (await client.GetFromJsonAsync<PagedTasks>($"/api/patrol-tasks?planId={target.Plan}"))!;
                Assert.Equal(0, filtered.Total); Assert.Empty(filtered.Items);
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/patrol-plans/{legacy.Plan}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/patrol-tasks/{legacy.Task}")).StatusCode);
        }
        var own = (await app.Ipqa.GetFromJsonAsync<PagedTasks>("/api/patrol-tasks/my?pageSize=1"))!;
        Assert.Equal(1, own.Total); Assert.Equal(legacy.Task, Assert.Single(own.Items).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Ipqa.GetAsync($"/api/patrol-tasks/{shared.Task}")).StatusCode);
        using var other = await TestAccounts.ClientAsync(app.Factory, "dev-ipqa-2");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/patrol-tasks/{shared.Task}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/patrol-tasks/{legacy.Task}")).StatusCode);
        Assert.Equal(before, await app.StateAsync());
    }

    [Fact]
    public async Task Completed_reads_keep_legacy_report_facts_and_never_export_shared_as_personally_assigned()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var legacy = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed");
        var path = $"/api/completed-patrol-records/{legacy.Task}";
        var original = await app.Admin.GetStringAsync(path + "/report");
        var shared = await app.SeedAsync(WorkflowVersion.LineSharedV1, "Completed");
        var before = await app.StateAsync();
        using var pqe = await TestAccounts.ClientAsync(app.Factory, "pqe");
        foreach (var client in new[] { app.Admin, pqe, app.Ipqa })
        {
            var list = (await client.GetFromJsonAsync<PagedCompletedRecords>("/api/completed-patrol-records?pageSize=1"))!;
            Assert.Equal(1, list.Total); Assert.Equal(legacy.Task, Assert.Single(list.Items).Id);
            Assert.Equal("dev-ipqa-1", list.Items[0].Inspector);
            Assert.Equal(original, await client.GetStringAsync(path));
            Assert.Equal(original, await client.GetStringAsync(path + "/report"));
            foreach (var suffix in new[] { "", "/report", "/report.xlsx" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/completed-patrol-records/{shared.Task}{suffix}")).StatusCode);
            using var download = await client.GetAsync(path + "/report.xlsx");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            using var stream = new MemoryStream(await download.Content.ReadAsByteArrayAsync());
            using var book = new ClosedXML.Excel.XLWorkbook(stream);
            Assert.Equal("dev-ipqa-1", book.Worksheet("Inspection Report").Cell(11, 2).GetString());
            Assert.Contains(book.Worksheet("Revision History").CellsUsed(), c => c.GetString() == "historical-inspector");
        }
        var report = (await app.Admin.GetFromJsonAsync<CompletedPatrolReport>(path))!;
        Assert.Equal("historical-inspector", report.FinalSubmittedBy);
        Assert.True(report.Items.Single().IsNa);
        Assert.Equal("historical-machine", report.Items.Single().MachineCode);
        Assert.Equal(WorkflowFixture.Now, report.FinalSubmittedAtUtc);
        Assert.Equal("Approved", Assert.Single(report.RevisionHistory).ReviewDecision);
        Assert.Equal(before, await app.StateAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void Unsupported_versions_never_classify_as_legacy(int version)
    {
        Assert.True(PatrolWorkflowPolicy.IsLegacy(WorkflowVersion.LegacyPersonalV1));
        Assert.Throws<UnsupportedWorkflowException>(() => PatrolWorkflowPolicy.RequireLegacy((WorkflowVersion)version));
    }

    [Fact]
    public async Task Plan_policy_precedes_validation_and_preserves_authorization_and_missing_semantics()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var invalid = new PlanInput(null, "", Guid.Empty, null, null, null, null, true, null, null, null, null, null);
        using var ipqa = await TestAccounts.ClientAsync(app.Factory, "dev-ipqa-1");
        foreach (var path in new[] { "enable", "disable", "delete", "update" })
        {
            async Task<HttpResponseMessage> Send(HttpClient client, Guid target) => path switch
            {
                "update" => await client.PutAsJsonAsync($"/api/patrol-plans/{target}", invalid),
                "delete" => await client.DeleteAsync($"/api/patrol-plans/{target}"),
                _ => await client.PostAsync($"/api/patrol-plans/{target}/{path}", null)
            };
            var before = await app.StateAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await Send(app.Admin, id.Plan)).StatusCode);
            Assert.Equal(before, await app.StateAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(app.Admin, Guid.NewGuid())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(ipqa, id.Plan)).StatusCode);
        }
    }

    [Theory]
    [InlineData("draft", "PendingInspection")]
    [InlineData("submit", "InProgress")]
    [InlineData("approve", "PendingApproval")]
    [InlineData("reject", "PendingApproval")]
    [InlineData("batch", "PendingApproval")]
    public async Task Assigned_LineShared_with_valid_legacy_state_cannot_use_legacy_mutations(string action, string status)
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LineSharedV1, status);
        var legacy = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, "PendingApproval");
        var before = await app.StateAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await app.MutateTaskAsync(action, id.Task, id.Item, legacy.Task)).StatusCode);
        Assert.Equal(before, await app.StateAsync());
    }

    [Fact]
    public async Task Ownerless_LineShared_is_persistable_without_fictional_users_or_assignees()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LineSharedV1, owner: false);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var task = await db.PatrolTasks.SingleAsync(x => x.Id == id.Task);
        Assert.Null(task.AssignedInspectorUserId);
        Assert.Equal("", task.AssignedInspectorKey);
        Assert.Empty(await db.PatrolPlanAssignees.Where(x => x.PatrolPlanId == id.Plan).ToListAsync());
        Assert.Equal(4, await db.ApplicationUsers.CountAsync());
    }

    [Theory]
    [InlineData("update")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task Plan_write_condition_rejects_policy_change_after_read(string action)
    {
        var race = new WorkflowRace();
        await using var app = await WorkflowFixture.CreateAsync(race);
        var id = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        // Delete must otherwise be eligible, and disabling starts enabled.
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            await db.PatrolTasks.Where(x => x.Id == id.Task).ExecuteDeleteAsync();
            await db.PatrolPlans.Where(x => x.Id == id.Plan).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsEnabled, action == "disable"));
        }
        var before = await app.StateAsync();
        race.Arm("PatrolPlans", id.Plan, id.Line);
        var response = action switch
        {
            "update" => await app.Admin.PutAsJsonAsync($"/api/patrol-plans/{id.Plan}", app.ValidPlan(id.Standard)),
            "delete" => await app.Admin.DeleteAsync($"/api/patrol-plans/{id.Plan}"),
            _ => await app.Admin.PostAsync($"/api/patrol-plans/{id.Plan}/{action}", null)
        };
        Assert.True(race.Fired);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await app.StateAsync());
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("batch")]
    public async Task Task_write_condition_rejects_policy_change_after_read_and_rolls_back_entire_batch(string action)
    {
        var race = new WorkflowRace();
        await using var app = await WorkflowFixture.CreateAsync(race);
        var status = action is "draft" or "submit" ? "InProgress" : "PendingApproval";
        var first = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, status);
        var second = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, status);
        var before = await app.StateAsync();
        // For batch, inject just before its SECOND task UPDATE, proving rollback of the first.
        race.Arm("PatrolTasks", second.Task, second.Line, action == "batch" ? 2 : 1);
        var response = await app.MutateTaskAsync(action, second.Task, second.Item, first.Task);
        Assert.True(race.Fired);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await app.StateAsync());
    }

    [Fact]
    public async Task Scheduler_skips_adversarial_shared_plan_and_freezes_legacy_task_identity()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var shared = await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var legacy = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        await db.PatrolPlans.ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEnabled, true));
        await db.PatrolTasks.ExecuteDeleteAsync();
        var generator = scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>();
        Assert.Equal(1, await generator.GenerateDueAsync());
        var generated = await db.PatrolTasks.AsNoTracking().SingleAsync();
        Assert.Equal(legacy.Plan, generated.PatrolPlanId);
        Assert.Equal(WorkflowVersion.LegacyPersonalV1, generated.WorkflowVersion);
        Assert.Equal(legacy.Line, generated.LineId);
        await db.PatrolPlans.Where(x => x.Id == legacy.Plan).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.WorkflowVersion, WorkflowVersion.LineSharedV1).SetProperty(x => x.LineId, shared.Line));
        Assert.Equal(0, await generator.GenerateDueAsync());
        var frozen = await db.PatrolTasks.AsNoTracking().SingleAsync();
        Assert.Equal(generated.WorkflowVersion, frozen.WorkflowVersion);
        Assert.Equal(generated.LineId, frozen.LineId);
        Assert.Equal(generated.LineNameSnapshot, frozen.LineNameSnapshot);
    }

    [Fact]
    public async Task Scheduler_rechecks_policy_at_insert_and_rolls_back_a_late_policy_change()
    {
        var race = new WorkflowRace();
        await using var app = await WorkflowFixture.CreateAsync(race);
        var id = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        await db.PatrolTasks.ExecuteDeleteAsync();
        await db.PatrolPlans.ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEnabled, true));
        var before = await app.StateAsync();
        race.Arm("PatrolPlans", id.Plan, id.Line);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>().GenerateDueAsync());
        Assert.True(race.Fired);
        Assert.Equal(before, await app.StateAsync());
    }

    [Fact]
    public async Task Forged_policy_fields_cannot_activate_shared_creation_or_change_legacy_identity()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        var json = System.Text.Json.JsonSerializer.SerializeToNode(app.ValidPlan(id.Standard))!;
        json["workflowVersion"] = 2;
        json["lineId"] = Guid.NewGuid().ToString();
        var response = await app.Admin.PutAsJsonAsync($"/api/patrol-plans/{id.Plan}", json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = app.Factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().PatrolPlans.AsNoTracking().SingleAsync();
        Assert.Equal(WorkflowVersion.LegacyPersonalV1, row.WorkflowVersion);
        Assert.Equal(id.Line, row.LineId);
        response = await app.Admin.PostAsJsonAsync("/api/patrol-plans", json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<PlanOutput>())!;
        var newRow = await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().PatrolPlans.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(WorkflowVersion.LegacyPersonalV1, newRow.WorkflowVersion);
        Assert.Null(newRow.LineId);
    }
}

// Deterministically interleave a policy change at the database write boundary after all pre-reads.
// The injected statement shares the transaction so rollback can be asserted for every table.
// This verifies conditional SQL; it is not represented as live PostgreSQL concurrency evidence.
internal sealed class WorkflowRace : DbCommandInterceptor
{
    private string? table;
    private Guid id, line;
    private int remaining;
    private bool followCommandTarget;
    public bool Fired { get; private set; }
    public void Arm(string targetTable, Guid target, Guid lineId, int writeNumber = 1)
    { table = targetTable; id = target; line = lineId; remaining = writeNumber; followCommandTarget = writeNumber > 1; Fired = false; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (table is not null && command.CommandText.StartsWith($"UPDATE \"{table}\"") && --remaining == 0)
        {
            var target = table; table = null;
            // IN-query result order is deliberately unspecified. Change the actual second UPDATE's
            // task, rather than assuming its identity from input-list order.
            if (followCommandTarget) id = command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<Guid>().Single();
            await using var change = command.Connection!.CreateCommand();
            change.Transaction = command.Transaction;
            change.CommandText = $"UPDATE \"{target}\" SET \"WorkflowVersion\" = 2, \"LineId\" = @line WHERE \"Id\" = @id";
            var p = change.CreateParameter(); p.ParameterName = "@line"; p.Value = line; change.Parameters.Add(p);
            p = change.CreateParameter(); p.ParameterName = "@id"; p.Value = id; change.Parameters.Add(p);
            Assert.Equal(1, await change.ExecuteNonQueryAsync(cancellationToken));
            Fired = true;
        }
        return result;
    }
}

internal sealed class WorkflowFixture : IAsyncDisposable
{
    public static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    public required WebApplicationFactory<Program> Factory { get; init; }
    public required HttpClient Admin { get; init; }
    public required HttpClient Ipqa { get; init; }
    public required string Path { get; init; }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    public static async Task<WorkflowFixture> CreateAsync(WorkflowRace? race = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ipqc-foundation-{Guid.NewGuid():N}.db");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.ConfigureLogging(x => x.ClearProviders());
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new Clock());
                services.AddDbContext<IpqcDbContext>(o => { o.UseSqlite($"Data Source={path}"); if (race is not null) o.AddInterceptors(race); });
            });
        });
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
        await TestAccounts.SeedAsync(factory);
        return new WorkflowFixture { Factory = factory, Path = path,
            Admin = await TestAccounts.ClientAsync(factory, "admin"), Ipqa = await TestAccounts.ClientAsync(factory, "dev-ipqa-1") };
    }
    public PlanInput ValidPlan(Guid standard) => new(null, "Updated", standard, "F", "Factory", "L", "Line", false,
        "2026-10-03T08:00", null, PatrolSchedule.PlantTimeZone, new ScheduleInput("daily", null, ["08:00"], null), TestAccounts.Ipqa1Id);
    public async Task<(Guid Plan, Guid Task, Guid Item, Guid Line, Guid Standard)> SeedAsync(WorkflowVersion version,
        string status = "PendingInspection", bool owner = true)
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var line = new ProductionLine { Id = Guid.NewGuid(), Code = $"TEST-{Guid.NewGuid():N}", Name = "Test line" };
        var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Test standard", LineName = "Legacy line", CreatedAtUtc = Now, UpdatedAtUtc = Now,
            InspectionItems = [new PatrolStandardItem { Id = Guid.NewGuid(), SequenceNo = 1, InspectionItem = "Visual", InspectionType = "Qualitative", SampleCount = "1" }] };
        var plan = new PatrolPlan { Id = Guid.NewGuid(), WorkflowVersion = version, Line = line, PlanNo = $"P-{Guid.NewGuid():N}", PlanName = "Test plan", PatrolStandard = standard,
            FactoryCode = "F", LineCode = "L", LineName = "Legacy line", EffectiveStartUtc = Now, GenerationNotBeforeUtc = Now.AddTicks(-1), CreatedAtUtc = Now, UpdatedAtUtc = Now,
            ScheduleDefinition = PatrolSchedule.Normalize(new ScheduleInput("daily", null, ["08:00"], null), new()) };
        if (owner) plan.Assignees.Add(new PatrolPlanAssignee { Id = Guid.NewGuid(), UserId = TestAccounts.Ipqa1Id, AssigneeKey = "dev-ipqa-1", IsActive = true });
        var item = new PatrolTaskItem { Id = Guid.NewGuid(), SequenceNo = 1, SourcePatrolStandardItemId = standard.InspectionItems[0].Id, InspectionItem = "Frozen visual",
            InspectionType = "Qualitative", SampleCount = "1", IsNa = true, JudgmentResult = "NA", InspectedAtUtc = Now, MachineCode = "historical-machine" };
        var submitted = status == "PendingApproval" || status == "Completed";
        var task = new PatrolTask { Id = Guid.NewGuid(), WorkflowVersion = version, Line = line, PatrolPlan = plan, PatrolStandard = standard, TaskNo = $"T-{Guid.NewGuid():N}",
            AssignedInspectorUserId = owner ? TestAccounts.Ipqa1Id : null, AssignedInspectorKey = owner ? "dev-ipqa-1" : "", Status = status, Shift = "Day", Items = [item],
            PlanNoSnapshot = plan.PlanNo, StandardNameSnapshot = "Frozen standard", LineNameSnapshot = "Frozen line", CurrentRevisionNo = submitted ? 1 : 0,
            ScheduledOccurrenceUtc = Now, GeneratedAtUtc = Now, CreatedAtUtc = Now, UpdatedAtUtc = Now, SubmittedAtUtc = submitted ? Now : null };
        if (submitted) task.Submissions.Add(new PatrolTaskSubmission { Id = Guid.NewGuid(), RevisionNo = 1, Shift = "Day", OverallInspectionResult = "Qualified", SubmittedBy = "historical-inspector", SubmittedAtUtc = Now,
            Items = [new PatrolTaskSubmissionItem { Id = Guid.NewGuid(), PatrolTaskItemId = item.Id, SequenceNo = 1, IsNa = true, JudgmentResult = "NA", InspectedAtUtc = Now, MachineCode = "historical-machine" }] });
        if (status == "Completed") { task.CompletedAtUtc = Now; task.Reviews.Add(new PatrolTaskReview { Id = Guid.NewGuid(), RevisionNo = 1, Decision = "Approved", Reviewer = "historical-reviewer", ReviewedAtUtc = Now }); }
        db.PatrolTasks.Add(task); await db.SaveChangesAsync(); return (plan.Id, task.Id, item.Id, line.Id, standard.Id);
    }
    public Task<HttpResponseMessage> MutateTaskAsync(string action, Guid task, Guid item, Guid other) => action switch
    {
        "draft" => Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task}/draft", new TaskDraftInput("Night", [new TaskItemDraft(item, false, [new TaskSampleDraft(null, "OK")])])),
        "submit" => Ipqa.PostAsync($"/api/patrol-tasks/{task}/submit", null),
        "reject" => Admin.PostAsJsonAsync($"/api/patrol-tasks/{task}/reject", new RejectTaskInput("Recheck")),
        "batch" => Admin.PostAsJsonAsync("/api/patrol-tasks/batch-approve", new BatchApproveInput([other, task])),
        _ => Admin.PostAsync($"/api/patrol-tasks/{task}/approve", null)
    };
    public async Task<string> StateAsync()
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var state = new object[] { await db.PatrolPlans.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.PatrolPlanAssignees.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            await db.PatrolTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.PatrolTaskItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            await db.PatrolTaskItemSamples.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.PatrolTaskSubmissions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            await db.PatrolTaskSubmissionItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.PatrolTaskReviews.AsNoTracking().OrderBy(x => x.Id).ToListAsync() };
        return System.Text.Json.JsonSerializer.Serialize(state);
    }
    public async ValueTask DisposeAsync()
    { Admin.Dispose(); Ipqa.Dispose(); await Factory.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(Path); }
}
