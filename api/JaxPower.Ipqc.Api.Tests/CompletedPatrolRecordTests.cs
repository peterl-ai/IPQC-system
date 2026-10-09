using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
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

public sealed class CompletedPatrolRecordTests
{
    private static readonly DateTime SpringDate = new(2026, 3, 8, 5, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Phase1E_database_upgrades_and_fresh_database_has_completed_query_index()
    {
        foreach (var upgrade in new[] { false, true })
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-record-migration-{Guid.NewGuid():N}.db");
            try
            {
                var options = new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options;
                await using var db = new IpqcDbContext(options);
                if (upgrade) await db.GetService<IMigrator>().MigrateAsync("20261004065509_Phase1ETaskExecutionApproval");
                await db.Database.MigrateAsync();
                Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("Phase1FCompletedRecordsReporting"));
                Assert.False(db.Database.HasPendingModelChanges());
                var indexes = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type='index' AND tbl_name='PatrolTasks'").ToListAsync();
                Assert.Contains("IX_PatrolTasks_Status_CompletedAtUtc", indexes);
            }
            finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }

    [Fact]
    public async Task Completed_list_filters_paginates_and_uses_plant_local_DST_dates()
    {
        await using var app = await Fixture.CreateAsync();
        await app.SeedAsync("PendingInspection", "PT-PENDING", SpringDate);
        await app.SeedAsync("InProgress", "PT-PROGRESS", SpringDate);
        await app.SeedAsync("PendingApproval", "PT-APPROVAL", SpringDate);
        await app.SeedAsync("Rejected", "PT-REJECTED", SpringDate);
        await app.SeedAsync("Completed", "PT-ONE", SpringDate);
        await app.SeedAsync("Completed", "PT-TWO", new DateTime(2026, 3, 9, 3, 30, 0, DateTimeKind.Utc));
        await app.SeedAsync("Completed", "PT-NEXT", new DateTime(2026, 3, 9, 4, 30, 0, DateTimeKind.Utc));
        var first = await app.Admin.GetFromJsonAsync<PagedCompletedRecords>("/api/completed-patrol-records?completedFrom=2026-03-08&completedTo=2026-03-08&pageSize=1");
        Assert.Equal(2, first!.Total);
        Assert.Single(first.Items);
        var second = await app.Admin.GetFromJsonAsync<PagedCompletedRecords>("/api/completed-patrol-records?completedFrom=2026-03-08&completedTo=2026-03-08&page=2&pageSize=1");
        Assert.Equal("PT-ONE", second!.Items.Single().TaskNo);
        Assert.Equal(DateTimeKind.Utc, second.Items.Single().CompletedAtUtc!.Value.Kind);
        var filtered = await app.Admin.GetFromJsonAsync<PagedCompletedRecords>("/api/completed-patrol-records?taskNo=PT-TWO&plan=1%2B1&standard=Standard&factory=Factory&line=Line&inspector=dev-ipqa-1&shift=Day&result=Qualified");
        Assert.Equal("PT-TWO", filtered!.Items.Single().TaskNo);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Admin.GetAsync("/api/completed-patrol-records?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Final_revision_detail_report_and_XLSX_use_approved_immutable_data_with_history_and_safe_text()
    {
        await using var app = await Fixture.CreateAsync();
        var id = await app.SeedAsync("Completed", "PT-FINAL", SpringDate, twoRevisions: true);
        foreach (var client in new[] { app.Admin, app.Pqe, app.Ipqa })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/completed-patrol-records")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/completed-patrol-records/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/completed-patrol-records/{id}/report")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/completed-patrol-records/{id}/report.xlsx")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-standards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{id}/approve", null)).StatusCode);
        var report = await app.Admin.GetFromJsonAsync<CompletedPatrolReport>($"/api/completed-patrol-records/{id}/report");
        Assert.NotNull(report);
        Assert.Equal(2, report.FinalRevisionNo);
        Assert.Equal("Qualified", report.Summary.OverallInspectionResult);
        Assert.Equal("P-FROZEN", report.Summary.PlanNo);
        Assert.Equal("Frozen Standard", report.Summary.StandardName);
        Assert.Equal(DateTimeKind.Utc, report.Summary.CompletedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, report.Items[1].InspectedAtUtc.Kind);
        Assert.Equal("=1+1", report.Summary.PlanName);
        Assert.Equal(4.5m, report.Items[0].Samples[0].InspectionValue);
        Assert.Equal(4.6m, report.Items[0].Samples[1].InspectionValue);
        Assert.True(report.Items[1].IsNa);
        Assert.Empty(report.Items[1].Samples);
        Assert.Equal("Rejected", report.RevisionHistory[0].ReviewDecision);
        Assert.Equal("Recheck measurement", report.RevisionHistory[0].RejectReason);
        Assert.Equal(7m, report.RevisionHistory[0].Items[0].Samples[0].InspectionValue);
        Assert.Equal("Approved", report.RevisionHistory[1].ReviewDecision);
        var response = await app.Admin.GetAsync($"/api/completed-patrol-records/{id}/report.xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("IPQC-PT-FINAL.xlsx", response.Content.Headers.ContentDisposition?.FileName);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var book = new XLWorkbook(stream);
        Assert.Equal(2, book.Worksheets.Count);
        var sheet = book.Worksheet("Inspection Report");
        Assert.Equal("PT-FINAL", sheet.Cell(3, 2).GetString());
        Assert.Equal("=1+1", sheet.Cell(5, 2).GetString());
        Assert.False(sheet.Cell(5, 2).HasFormula);
        Assert.Equal("Qualified", sheet.Cell(17, 2).GetString());
        Assert.Contains(sheet.CellsUsed(), x => x.GetString() == "4.5");
        Assert.Contains(sheet.CellsUsed(), x => x.GetString() == "Yes");
        Assert.Contains(sheet.CellsUsed(), x => x.GetString() == "@suspicious");
        Assert.Contains(sheet.CellsUsed(), x => x.GetString() == "+cmd");
        Assert.Contains(sheet.CellsUsed(), x => x.GetString() == "-cmd");
        Assert.Equal("Yes", sheet.Cell(25, 10).GetString());
        Assert.Equal("", sheet.Cell(25, 13).GetString());
        Assert.All(sheet.CellsUsed(), x => Assert.False(x.HasFormula));
        var history = book.Worksheet("Revision History");
        Assert.Contains(history.CellsUsed(), x => x.GetString() == "Recheck measurement");
        Assert.Contains(history.CellsUsed(), x => x.GetString().StartsWith("7", StringComparison.Ordinal));
        Assert.Contains(history.CellsUsed(), x => x.GetString() == "Approved");
    }

    [Fact]
    public async Task Approved_unqualified_is_reported_and_inconsistent_final_revision_returns_conflict()
    {
        await using var app = await Fixture.CreateAsync();
        var unqualified = await app.SeedAsync("Completed", "PT-NG", SpringDate, overall: "Unqualified");
        var report = await app.Admin.GetFromJsonAsync<CompletedPatrolReport>($"/api/completed-patrol-records/{unqualified}/report");
        Assert.Equal("Unqualified", report!.Summary.OverallInspectionResult);
        var workbookResponse = await app.Admin.GetAsync($"/api/completed-patrol-records/{unqualified}/report.xlsx");
        using (var stream = new MemoryStream(await workbookResponse.Content.ReadAsByteArrayAsync()))
        using (var workbook = new XLWorkbook(stream))
            Assert.Equal("Unqualified", workbook.Worksheet("Inspection Report").Cell(17, 2).GetString());
        var broken = await app.SeedAsync("Completed", "PT-BROKEN", SpringDate, twoRevisions: true, missingReview: true);
        await app.SetFirstReviewApprovedAsync(broken);
        foreach (var path in new[] { $"/api/completed-patrol-records/{broken}",
            $"/api/completed-patrol-records/{broken}/report", $"/api/completed-patrol-records/{broken}/report.xlsx" })
            Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Admin.GetAsync($"/api/completed-patrol-records/{Guid.NewGuid()}")).StatusCode);
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, string path) : IAsyncDisposable
    {
        public HttpClient Admin { get; } = Client(factory, "admin");
        public HttpClient Pqe { get; } = Client(factory, "pqe");
        public HttpClient Ipqa { get; } = Client(factory, "ipqa");
        private static HttpClient Client(WebApplicationFactory<Program> factory, string role)
        {
            var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Dev-Role", role); return client;
        }
        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-record-{Guid.NewGuid():N}.db");
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development"); web.ConfigureLogging(x => x.ClearProviders());
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                    services.AddDbContext<IpqcDbContext>(options => options.UseSqlite($"Data Source={path}"));
                });
            });
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
            return new Fixture(factory, path);
        }
        public async Task<Guid> SeedAsync(string status, string taskNo, DateTime completed,
            bool twoRevisions = false, bool missingReview = false, string overall = "Qualified")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Current Standard", LineName = "Line",
                CreatedAtUtc = completed, UpdatedAtUtc = completed };
            var plan = new PatrolPlan { Id = Guid.NewGuid(), PlanNo = taskNo, PlanName = "Current Plan",
                PatrolStandard = standard, EffectiveStartUtc = completed, ScheduleDefinition = "{}",
                GenerationNotBeforeUtc = completed, CreatedAtUtc = completed, UpdatedAtUtc = completed };
            var task = new PatrolTask { Id = Guid.NewGuid(), TaskNo = taskNo, PatrolPlan = plan, PatrolStandard = standard,
                AssignedInspectorKey = "dev-ipqa-1", ScheduledOccurrenceUtc = completed.AddHours(-2),
                GeneratedAtUtc = completed.AddHours(-2), Status = status,
                PlanNoSnapshot = "P-FROZEN", PlanNameSnapshot = "=1+1", StandardNameSnapshot = "Frozen Standard",
                FactoryNameSnapshot = "Factory", LineNameSnapshot = "Line", Shift = "Day",
                OverallInspectionResult = overall, CurrentRevisionNo = status == "Completed" ? (twoRevisions ? 2 : 1) : 0,
                SubmittedAtUtc = status == "Completed" ? completed.AddHours(-1) : null,
                CompletedAtUtc = status == "Completed" ? completed : null,
                CreatedAtUtc = completed.AddHours(-2), UpdatedAtUtc = completed };
            if (status == "Completed")
            {
                var measure = new PatrolTaskItem { Id = Guid.NewGuid(), PatrolTask = task, SourcePatrolStandardItemId = Guid.NewGuid(),
                    SequenceNo = 1, ProcessCode = "P", ProcessName = "Process", InspectionItem = "Measure",
                    InspectionContent = "@suspicious", InspectionType = "Quantitative", SampleCount = "2",
                    LowerLimitOperator = ">=", LowerLimitValue = "3", UpperLimitOperator = "<=", UpperLimitValue = "6" };
                var na = new PatrolTaskItem { Id = Guid.NewGuid(), PatrolTask = task, SourcePatrolStandardItemId = Guid.NewGuid(),
                    SequenceNo = 2, InspectionItem = "Visual", InspectionType = "Qualitative", SampleCount = "1" };
                task.Items.AddRange([measure, na]);
                if (twoRevisions) AddRevision(task, measure, na, 1, completed.AddHours(-1), "Unqualified", 7m, "Rejected", "Recheck measurement");
                AddRevision(task, measure, na, twoRevisions ? 2 : 1, completed, overall,
                    overall == "Unqualified" ? 7m : 4.5m, missingReview ? null : "Approved", null);
            }
            db.PatrolTasks.Add(task);
            await db.SaveChangesAsync();
            return task.Id;
        }
        private static void AddRevision(PatrolTask task, PatrolTaskItem measure, PatrolTaskItem na,
            int revision, DateTime at, string overall, decimal value, string? decision, string? reason)
        {
            task.Submissions.Add(new PatrolTaskSubmission { Id = Guid.NewGuid(), PatrolTask = task,
                RevisionNo = revision, Shift = "Day", OverallInspectionResult = overall,
                SubmittedBy = "dev-ipqa-1", SubmittedAtUtc = at,
                Items = [
                    new PatrolTaskSubmissionItem { Id = Guid.NewGuid(), PatrolTaskItemId = measure.Id,
                        SequenceNo = 1, JudgmentResult = value > 6 ? "NG" : "OK", InspectedAtUtc = at.AddMinutes(-5),
                        Remarks = "+cmd", AbnormalCause = "-cmd", Samples = [
                            new PatrolTaskSubmissionSample { Id = Guid.NewGuid(), SequenceNo = 1, InspectionValue = value,
                                JudgmentResult = value > 6 ? "NG" : "OK", InspectedAtUtc = at.AddMinutes(-6) },
                            new PatrolTaskSubmissionSample { Id = Guid.NewGuid(), SequenceNo = 2, InspectionValue = 4.6m,
                                JudgmentResult = "OK", InspectedAtUtc = at.AddMinutes(-5) }] },
                    new PatrolTaskSubmissionItem { Id = Guid.NewGuid(), PatrolTaskItemId = na.Id,
                        SequenceNo = 2, IsNa = true, JudgmentResult = "NA", InspectedAtUtc = at.AddMinutes(-4) }
                ] });
            if (decision is not null) task.Reviews.Add(new PatrolTaskReview { Id = Guid.NewGuid(), PatrolTask = task,
                RevisionNo = revision, Decision = decision, Reason = reason, Reviewer = "pqe", ReviewedAtUtc = at });
        }
        public async Task SetFirstReviewApprovedAsync(Guid taskId)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var review = await db.PatrolTaskReviews.SingleAsync(x => x.PatrolTaskId == taskId && x.RevisionNo == 1);
            review.Decision = "Approved";
            review.Reason = null;
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose(); Pqe.Dispose(); Ipqa.Dispose(); await factory.DisposeAsync();
            SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path);
        }
    }
}
