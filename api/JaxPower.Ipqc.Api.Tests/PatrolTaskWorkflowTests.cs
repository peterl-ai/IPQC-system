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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PatrolTaskWorkflowTests
{
    private static readonly DateTime Start = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static ItemInput Item(string name, string type = "Qualitative", string count = "1",
        string lowerOp = "", string lowerValue = "", string upperOp = "", string upperValue = "", Guid? id = null) =>
        new("P", "Process", "Category", name, $"Inspect {name}", upperOp, upperValue,
            lowerOp, lowerValue, type, "Every sample", count, "Optional", "Major", id);

    [Fact]
    public async Task Phase1D_database_upgrades_without_inventing_legacy_task_snapshots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ipqc-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options;
            await using (var oldDb = new IpqcDbContext(options))
            {
                await oldDb.GetService<IMigrator>().MigrateAsync("20261004001954_PatrolPlansAndScheduler");
                var now = Start;
                var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Old Standard",
                    LineName = "Line", CreatedAtUtc = now, UpdatedAtUtc = now };
                var plan = new PatrolPlan { Id = Guid.NewGuid(), PlanNo = "OLD-PLAN", PlanName = "Old Plan",
                    PatrolStandardId = standard.Id, EffectiveStartUtc = now, ScheduleDefinition = "{}",
                    GenerationNotBeforeUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
                oldDb.PatrolStandards.Add(standard);
                oldDb.PatrolPlans.Add(plan);
                await oldDb.SaveChangesAsync();
                var oldTaskId = Guid.NewGuid();
                await oldDb.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO PatrolTasks (Id, TaskNo, PatrolPlanId, PatrolStandardId, AssignedInspectorKey,
                        ScheduledOccurrenceUtc, GeneratedAtUtc, GenerationSource, Status, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({oldTaskId}, {"OLD-TASK"}, {plan.Id}, {standard.Id}, {"dev-ipqa-1"},
                        {now}, {now}, {"Scheduler"}, {"PendingInspection"}, {now}, {now})
                    """);
            }
            await using (var upgradedDb = new IpqcDbContext(options))
            {
                await upgradedDb.Database.MigrateAsync();
                var task = await upgradedDb.PatrolTasks.SingleAsync();
                Assert.Null(task.PlanNoSnapshot);
                Assert.Null(task.StandardNameSnapshot);
                Assert.Empty(await upgradedDb.PatrolTaskItems.ToListAsync());
                Assert.Contains("20261004065509_Phase1ETaskExecutionApproval",
                    await upgradedDb.Database.GetAppliedMigrationsAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Generation_freezes_ordered_header_and_item_definitions()
    {
        await using var app = await Fixture.CreateAsync();
        var standard = await app.StandardAsync("Original", [Item("First", "Quantitative", "2", ">=", "3", "<=", "6"), Item("Second")]);
        var plan = await app.PlanAsync(standard.Id);
        Assert.Equal(1, await app.GenerateAsync());
        var first = (await app.TasksAsync()).Single();
        var frozen = await app.DetailAsync(first.Id);
        Assert.Equal(plan.PlanNo, frozen.Summary.PlanNoSnapshot);
        Assert.Equal("Original", frozen.Summary.StandardNameSnapshot);
        Assert.Equal(["First", "Second"], frozen.Items.Select(x => x.InspectionItem));
        Assert.All(frozen.Items, x => Assert.NotEqual(x.Id, x.SourcePatrolStandardItemId));
        Assert.All(frozen.Items, x => Assert.NotEqual(Guid.Empty, x.Id));

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.Zero);
        var changed = new StandardInput("Changed", "FC", "Factory", "WC", "LC", "Line", "MC",
            [Item("Second edited", id: standard.InspectionItems[1].Id),
             Item("First", "Quantitative", "2", ">", "4", "<", "7", standard.InspectionItems[0].Id),
             Item("Third")]);
        var update = await app.Admin.PutAsJsonAsync($"/api/patrol-standards/{standard.Id}", changed);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, await app.GenerateAsync());
        var firstAgain = await app.DetailAsync(first.Id);
        var second = await app.DetailAsync((await app.TasksAsync()).Single(x => x.Id != first.Id).Id);
        Assert.Equal("Original", firstAgain.Summary.StandardNameSnapshot);
        Assert.Equal(["First", "Second"], firstAgain.Items.Select(x => x.InspectionItem));
        Assert.Equal(">=", firstAgain.Items[0].LowerLimitOperator);
        Assert.Equal("3", firstAgain.Items[0].LowerLimitValue);
        Assert.Equal("Changed", second.Summary.StandardNameSnapshot);
        Assert.Equal(["Second edited", "First", "Third"], second.Items.Select(x => x.InspectionItem));
        Assert.Equal(">", second.Items[1].LowerLimitOperator);
        Assert.Equal("4", second.Items[1].LowerLimitValue);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.Equal(2, await db.PatrolTaskItems.CountAsync(x => x.PatrolTaskId == first.Id));
        Assert.Equal(3, await db.PatrolTaskItems.CountAsync(x => x.PatrolTaskId == second.Summary.Id));
    }

    [Fact]
    public async Task Draft_submit_reject_resubmit_and_approve_preserve_revisions()
    {
        await using var app = await Fixture.CreateAsync();
        var standard = await app.StandardAsync("Standard", [Item("Measure", "Quantitative", "1", ">=", "3", "<=", "6"), Item("Visual")]);
        await app.PlanAsync(standard.Id);
        Assert.Equal(1, await app.GenerateAsync());
        var task = await app.DetailAsync((await app.TasksAsync()).Single().Id);
        var measure = task.Items[0].Id;
        var visual = task.Items[1].Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await app.OtherIpqa.GetAsync($"/api/patrol-tasks/{task.Summary.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Pqe.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);

        var partial = await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput("Day", [new TaskItemDraft(measure, false, [new TaskSampleDraft(4.5m, "NG")])]));
        Assert.True(partial.StatusCode == HttpStatusCode.OK, await partial.Content.ReadAsStringAsync());
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("InProgress", task.Summary.Status);
        Assert.Equal("OK", task.Items[0].Samples[0].JudgmentResult); // client NG ignored
        Assert.Empty(task.Submissions);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        var na = await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput(null, [new TaskItemDraft(visual, true, [])]));
        Assert.True(na.StatusCode == HttpStatusCode.OK, await na.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("PendingApproval", task.Summary.Status);
        Assert.Equal("Qualified", task.Submissions[0].OverallInspectionResult);
        Assert.Equal("NA", task.Submissions[0].Items[1].JudgmentResult);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", new TaskDraftInput("Night", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Pqe.PostAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/reject", new RejectTaskInput("   "))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Pqe.PostAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/reject", new RejectTaskInput("  Verify label  "))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Pqe.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput("Night", [new TaskItemDraft(measure, false, [new TaskSampleDraft(7m, "OK")])]))).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("Rejected", task.Summary.Status);
        Assert.True(task.Items[0].JudgmentResult == "NG", System.Text.Json.JsonSerializer.Serialize(task.Items[0]));
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Pqe.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/approve", null)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("Completed", task.Summary.Status);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal(2, task.Summary.CurrentRevisionNo);
        Assert.Equal(2, task.Submissions.Count);
        Assert.Equal("Qualified", task.Submissions[0].OverallInspectionResult);
        Assert.Equal(4.5m, task.Submissions[0].Items[0].Samples[0].InspectionValue);
        Assert.Equal("Rejected", task.Submissions[0].Review!.Decision);
        Assert.Equal("Verify label", task.Submissions[0].Review!.Reason);
        Assert.Equal("Unqualified", task.Submissions[1].OverallInspectionResult);
        Assert.Equal(7m, task.Submissions[1].Items[0].Samples[0].InspectionValue);
        Assert.Equal("Approved", task.Submissions[1].Review!.Decision);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", new TaskDraftInput("Day", []))).StatusCode);
    }

    [Fact]
    public async Task Inspection_times_are_stable_complete_and_frozen_per_revision()
    {
        await using var app = await Fixture.CreateAsync();
        var standard = await app.StandardAsync("Audit Standard",
            [Item("Visual"), Item("Measure", "Quantitative", "1", ">=", "3", "<=", "6"), Item("Not applicable")]);
        await app.PlanAsync(standard.Id);
        await app.GenerateAsync();
        var task = await app.DetailAsync((await app.TasksAsync()).Single().Id);
        var qualitative = task.Items[0].Id;
        var quantitative = task.Items[1].Id;
        var na = task.Items[2].Id;

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 12, 10, 0, TimeSpan.Zero);
        var firstInspection = app.Clock.Now.UtcDateTime;
        var initial = new TaskDraftInput("Day",
        [
            new TaskItemDraft(qualitative, false, [new TaskSampleDraft(null, "OK")]),
            new TaskItemDraft(quantitative, false, [new TaskSampleDraft(4.5m, null)]),
            new TaskItemDraft(na, true, [])
        ]);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", initial)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal(firstInspection, task.Items[0].InspectedAtUtc);
        Assert.Equal(firstInspection, task.Items[1].InspectedAtUtc);
        Assert.Equal(firstInspection, task.Items[2].InspectedAtUtc);
        Assert.Equal(firstInspection, task.Items[0].Samples.Single().InspectedAtUtc);
        Assert.Equal(firstInspection, task.Items[1].Samples.Single().InspectedAtUtc);
        Assert.Empty(task.Items[2].Samples);

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 12, 20, 0, TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", initial)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.All(task.Items, item => Assert.Equal(firstInspection, item.InspectedAtUtc));
        Assert.Equal(firstInspection, task.Items[0].Samples.Single().InspectedAtUtc);
        Assert.Equal(firstInspection, task.Items[1].Samples.Single().InspectedAtUtc);

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 12, 30, 0, TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput(null, [new TaskItemDraft(quantitative, false, [new TaskSampleDraft(null, null)])]))).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Null(task.Items[1].InspectedAtUtc);
        Assert.Null(task.Items[1].Samples.Single().InspectedAtUtc);

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 12, 40, 0, TimeSpan.Zero);
        var restoredAt = app.Clock.Now.UtcDateTime;
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput(null, [new TaskItemDraft(quantitative, false, [new TaskSampleDraft(4.5m, null)])]))).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal(restoredAt, task.Items[1].InspectedAtUtc);
        Assert.Equal(restoredAt, task.Items[1].Samples.Single().InspectedAtUtc);

        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 12, 50, 0, TimeSpan.Zero);
        var revisionOneInspection = app.Clock.Now.UtcDateTime;
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput(null,
            [
                new TaskItemDraft(qualitative, false, [new TaskSampleDraft(null, "NG")]),
                new TaskItemDraft(quantitative, false, [new TaskSampleDraft(5m, null)]),
                new TaskItemDraft(na, true, [])
            ]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[0].InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[0].Samples.Single().InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[1].InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[1].Samples.Single().InspectedAtUtc);
        Assert.Equal(firstInspection, task.Submissions[0].Items[2].InspectedAtUtc);
        Assert.Empty(task.Submissions[0].Items[2].Samples);

        Assert.Equal(HttpStatusCode.NoContent, (await app.Pqe.PostAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/reject",
            new RejectTaskInput("Reinspect"))).StatusCode);
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.Zero);
        var revisionTwoInspection = app.Clock.Now.UtcDateTime;
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft",
            new TaskDraftInput(null,
            [
                new TaskItemDraft(qualitative, false, [new TaskSampleDraft(null, "OK")]),
                new TaskItemDraft(quantitative, false, [new TaskSampleDraft(4m, null)])
            ]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal(2, task.Submissions.Count);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[0].InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[0].Samples.Single().InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[1].InspectedAtUtc);
        Assert.Equal(revisionOneInspection, task.Submissions[0].Items[1].Samples.Single().InspectedAtUtc);
        Assert.Equal(revisionTwoInspection, task.Submissions[1].Items[0].InspectedAtUtc);
        Assert.Equal(revisionTwoInspection, task.Submissions[1].Items[0].Samples.Single().InspectedAtUtc);
        Assert.Equal(revisionTwoInspection, task.Submissions[1].Items[1].InspectedAtUtc);
        Assert.Equal(revisionTwoInspection, task.Submissions[1].Items[1].Samples.Single().InspectedAtUtc);
    }

    [Fact]
    public async Task Sample_count_qualitative_and_na_rules_are_enforced()
    {
        await using var app = await Fixture.CreateAsync();
        var standard = await app.StandardAsync("Standard", [Item("Visual", "Qualitative", "2")]);
        await app.PlanAsync(standard.Id);
        await app.GenerateAsync();
        var task = await app.DetailAsync((await app.TasksAsync()).Single().Id);
        var id = task.Items[0].Id;
        await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", new TaskDraftInput("Day",
            [new TaskItemDraft(id, false, [new TaskSampleDraft(null, "OK")])]));
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
        await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", new TaskDraftInput(null,
            [new TaskItemDraft(id, false, [new TaskSampleDraft(null, "OK"), new TaskSampleDraft(null, "NG")])]));
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("NG", task.Items[0].JudgmentResult);
        await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Summary.Id}/draft", new TaskDraftInput(null,
            [new TaskItemDraft(id, true, [])]));
        task = await app.DetailAsync(task.Summary.Id);
        Assert.Equal("NA", task.Items[0].JudgmentResult);
        Assert.Empty(task.Items[0].Samples);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Summary.Id}/submit", null)).StatusCode);
    }

    [Fact]
    public void Quantitative_comparisons_and_bad_configuration_are_server_authoritative()
    {
        var service = new InspectionJudgmentService();
        var item = new PatrolTaskItem { Id = Guid.NewGuid(), SequenceNo = 1, InspectionType = "Quantitative",
            LowerLimitOperator = ">=", LowerLimitValue = "3", UpperLimitOperator = "<=", UpperLimitValue = "6", IsNa = false };
        var task = new PatrolTask { Shift = "Day", Items = [item] };
        foreach (var (value, expected) in new[] { (1m, "NG"), (3m, "OK"), (4.5m, "OK"), (6m, "OK"), (7m, "NG") })
        {
            item.Samples = [new PatrolTaskItemSample { SequenceNo = 1, InspectionValue = value, JudgmentResult = "OK" }];
            Assert.Empty(service.Calculate(task, true));
            Assert.Equal(expected, item.JudgmentResult);
            Assert.Equal(expected, item.Samples[0].JudgmentResult);
        }
        item.Samples = [new PatrolTaskItemSample { SequenceNo = 1 }];
        Assert.NotEmpty(service.Calculate(task, true));
        Assert.Null(item.JudgmentResult);
        Assert.Null(item.Samples[0].JudgmentResult);
        item.LowerLimitOperator = ">";
        item.UpperLimitOperator = "<";
        item.Samples = [new PatrolTaskItemSample { SequenceNo = 1, InspectionValue = 3m }];
        service.Calculate(task, true);
        Assert.Equal("NG", item.JudgmentResult);
        item.Samples[0].InspectionValue = 6m;
        service.Calculate(task, true);
        Assert.Equal("NG", item.JudgmentResult);
        item.LowerLimitValue = "broken";
        Assert.NotEmpty(service.Calculate(task, true));
        Assert.Null(item.JudgmentResult);
        item.LowerLimitOperator = "";
        item.LowerLimitValue = "";
        item.UpperLimitOperator = "";
        item.UpperLimitValue = "";
        Assert.NotEmpty(service.Calculate(task, true));
    }

    [Fact]
    public async Task Review_race_and_batch_validation_leave_one_decision_per_revision()
    {
        await using var app = await Fixture.CreateAsync();
        var standard = await app.StandardAsync("Standard", [Item("Visual")]);
        await app.PlanAsync(standard.Id);
        await app.GenerateAsync();
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, await app.GenerateAsync());
        var tasks = await app.TasksAsync();
        foreach (var task in tasks)
        {
            var detail = await app.DetailAsync(task.Id);
            var draftResponse = await app.Ipqa.PutAsJsonAsync($"/api/patrol-tasks/{task.Id}/draft", new TaskDraftInput("Day",
                [new TaskItemDraft(detail.Items[0].Id, false, [new TaskSampleDraft(null, "OK")])]));
            Assert.True(draftResponse.StatusCode == HttpStatusCode.OK, await draftResponse.Content.ReadAsStringAsync());
            var submitResponse = await app.Ipqa.PostAsync($"/api/patrol-tasks/{task.Id}/submit", null);
            Assert.True(submitResponse.StatusCode == HttpStatusCode.OK, await submitResponse.Content.ReadAsStringAsync());
        }
        var first = tasks[0].Id;
        var competing = await Task.WhenAll(
            app.Pqe.PostAsync($"/api/patrol-tasks/{first}/approve", null),
            app.Admin.PostAsJsonAsync($"/api/patrol-tasks/{first}/reject", new RejectTaskInput("Recheck")));
        Assert.Contains(competing, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Contains(competing, x => x.StatusCode == HttpStatusCode.Conflict);
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            Assert.Equal(1, await db.PatrolTaskReviews.CountAsync(x => x.PatrolTaskId == first));
        }
        var pending = tasks.Skip(1).Select(x => x.Id).ToList();
        Assert.Equal(HttpStatusCode.Conflict, (await app.Pqe.PostAsJsonAsync("/api/patrol-tasks/batch-approve",
            new BatchApproveInput([first, pending[0]]))).StatusCode);
        Assert.Equal("PendingApproval", (await app.DetailAsync(pending[0])).Summary.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Pqe.PostAsJsonAsync("/api/patrol-tasks/batch-approve",
            new BatchApproveInput(Enumerable.Repeat(Guid.NewGuid(), 51).ToList()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Pqe.PostAsJsonAsync("/api/patrol-tasks/batch-approve",
            new BatchApproveInput(pending))).StatusCode);
        foreach (var id in pending) Assert.Equal("Completed", (await app.DetailAsync(id)).Summary.Status);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(Start);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, string path, ManualClock clock) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public ManualClock Clock { get; } = clock;
        public HttpClient Admin { get; } = Client(factory, "admin");
        public HttpClient Pqe { get; } = Client(factory, "pqe");
        public HttpClient Ipqa { get; } = Client(factory, "ipqa", "dev-ipqa-1");
        public HttpClient OtherIpqa { get; } = Client(factory, "ipqa", "dev-ipqa-2");

        private static HttpClient Client(WebApplicationFactory<Program> factory, string role, string? assignee = null)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Dev-Role", role);
            if (assignee is not null) client.DefaultRequestHeaders.Add("X-Dev-Assignee-Key", assignee);
            return client;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-task-{Guid.NewGuid():N}.db");
            var clock = new ManualClock();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureLogging(x => x.ClearProviders());
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddDbContext<IpqcDbContext>(options => options.UseSqlite($"Data Source={path}"));
                });
            });
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
            return new Fixture(factory, path, clock);
        }

        public async Task<StandardOutput> StandardAsync(string name, List<ItemInput> items)
        {
            var response = await Admin.PostAsJsonAsync("/api/patrol-standards", new StandardInput(name,
                "FC", "Factory", "WC", "LC", "Line", "MC", items));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<StandardOutput>())!;
        }
        public async Task<PlanOutput> PlanAsync(Guid standardId)
        {
            var response = await Admin.PostAsJsonAsync("/api/patrol-plans", new PlanInput(null,
                "Line Plan", standardId, "FC", "Factory", "LC", "Line", true,
                "2026-10-03T08:00", null, PatrolSchedule.PlantTimeZone,
                new ScheduleInput("everyNHours", 2, null, null), "dev-ipqa-1"));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PlanOutput>())!;
        }
        public async Task<int> GenerateAsync()
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>().GenerateDueAsync();
        }
        public async Task<List<TaskSummary>> TasksAsync() =>
            (await Admin.GetFromJsonAsync<PagedTasks>("/api/patrol-tasks?pageSize=100"))!.Items;
        public async Task<TaskDetail> DetailAsync(Guid id) =>
            (await Admin.GetFromJsonAsync<TaskDetail>($"/api/patrol-tasks/{id}"))!;
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose(); Pqe.Dispose(); Ipqa.Dispose(); OtherIpqa.Dispose();
            await Factory.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
