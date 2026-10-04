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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PatrolPlansAndSchedulerTests
{
    private static readonly DateTime InitialUtc = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static ScheduleInput Hours(int n = 2) => new("everyNHours", n, null, null);
    private static PlanInput Draft(Guid standardId, ScheduleInput? schedule = null, bool enabled = true) =>
        new(null, "Line A Patrol", standardId, "F1", "Factory", "L1", "Line A", enabled,
            "2026-10-03T08:00", null, PatrolSchedule.PlantTimeZone, schedule ?? Hours(), "dev-ipqa-1");

    [Fact]
    public async Task Plan_crud_filters_and_deletion_are_persistent()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var input = Draft(standard);
        var created = await app.CreateAsync(input);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.StartsWith("PLN-", created.PlanNo);
        Assert.Equal("admin", created.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAtUtc.Kind);
        Assert.Equal("dev-ipqa-1", created.AssigneeKey);
        Assert.Equal("America/New_York", created.TimeZoneId);
        var got = await app.Client.GetFromJsonAsync<PlanOutput>($"/api/patrol-plans/{created.Id}");
        Assert.Equal(created.Id, got!.Id);
        var listed = await app.Client.GetFromJsonAsync<PagedPlans>("/api/patrol-plans?planName=Line%20A&factoryCode=F1&lineCode=L1&enabled=true&page=1&pageSize=5");
        Assert.Single(listed!.Items);
        Assert.Equal(1, listed.Total);
        Assert.Equal(standard, listed.Items[0].PatrolStandardId);
        Assert.Equal("Line A Patrol", listed.Items[0].PlanName);
        var changed = input with { PlanName = "Updated", PlanNo = created.PlanNo };
        var response = await app.Client.PutAsJsonAsync($"/api/patrol-plans/{created.Id}", changed);
        response.EnsureSuccessStatusCode();
        Assert.Equal("Updated", (await response.Content.ReadFromJsonAsync<PlanOutput>())!.PlanName);
        Assert.Equal(0, (await app.Client.GetFromJsonAsync<PagedPlans>("/api/patrol-plans?planName=Missing"))!.Total);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client.DeleteAsync($"/api/patrol-plans/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/patrol-plans/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Validation_and_role_are_server_authoritative()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var bad = Draft(Guid.NewGuid()) with { PlanName = "  ", EffectiveEndLocal = "2026-10-03T07:00", AssigneeKey = null,
            Schedule = new("cron", null, null, null) };
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-plans", bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-plans", Draft(standard) with { TimeZoneId = "UTC" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-plans", Draft(standard) with { Schedule = new("daily", null, [], null) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-plans", Draft(standard) with { AssigneeKey = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-plans", Draft(standard) with { EffectiveStartLocal = "2026-03-08T02:30" })).StatusCode);
        var ipqa = app.Factory.CreateClient();
        ipqa.DefaultRequestHeaders.Add("X-Dev-Role", "ipqa");
        Assert.Equal(HttpStatusCode.Forbidden, (await ipqa.GetAsync("/api/patrol-plans")).StatusCode);
        var pqe = app.Factory.CreateClient();
        pqe.DefaultRequestHeaders.Add("X-Dev-Role", "pqe");
        Assert.Equal(HttpStatusCode.OK, (await pqe.GetAsync("/api/patrol-plans/assignees")).StatusCode);
    }

    [Fact]
    public void Recurrence_normalizes_and_handles_dst_deterministically()
    {
        var errors = new Dictionary<string, string[]>();
        var daily = PatrolSchedule.Read(PatrolSchedule.Normalize(new("daily", null, ["16:00", "08:00", "08:00"], null), errors));
        Assert.Empty(errors);
        Assert.Equal(["08:00", "16:00"], daily.Times);
        var weekly = PatrolSchedule.Read(PatrolSchedule.Normalize(new("weekly", null, ["08:00"], ["Friday", "Monday", "Monday"]), errors));
        Assert.Equal(["Monday", "Friday"], weekly.DaysOfWeek);
        var start = new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc);
        var spring = PatrolSchedule.Occurrences(new("daily", null, ["02:30"], null), PatrolSchedule.PlantTimeZone,
            start, start, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal([new DateTime(2026, 3, 7, 7, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 9, 6, 30, 0, DateTimeKind.Utc)], spring);
        var fallStart = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        var fall = PatrolSchedule.Occurrences(new("daily", null, ["01:30"], null), PatrolSchedule.PlantTimeZone,
            fallStart, fallStart, new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), fall);
        Assert.DoesNotContain(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), fall);
        Assert.Equal(3, fall.Count);
        var hours = PatrolSchedule.Occurrences(Hours(), PatrolSchedule.PlantTimeZone,
            new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 17, 0, 0, DateTimeKind.Utc));
        Assert.Equal([new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 16, 0, 0, DateTimeKind.Utc)], hours);
        var monday = PatrolSchedule.Occurrences(weekly, PatrolSchedule.PlantTimeZone,
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal([new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)], monday);
    }

    [Fact]
    public async Task Generation_catches_up_is_idempotent_and_preserves_history()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var plan = await app.CreateAsync(Draft(standard));
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(4, await app.GenerateAsync()); // 08:00, 10:00, 12:00, 14:00 local
        Assert.Equal(0, await app.GenerateAsync());
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var tasks = await db.PatrolTasks.AsNoTracking().OrderBy(x => x.ScheduledOccurrenceUtc).ToListAsync();
            Assert.Equal(4, tasks.Count);
            Assert.All(tasks, x => { Assert.Equal("PendingInspection", x.Status); Assert.Equal(standard, x.PatrolStandardId); Assert.Equal("dev-ipqa-1", x.AssignedInspectorKey); Assert.Equal("Scheduler", x.GenerationSource); });
            Assert.Equal(plan.Id, tasks[0].PatrolPlanId);
            Assert.Equal(app.Clock.Now.UtcDateTime, tasks[0].GeneratedAtUtc);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/patrol-plans/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/patrol-standards/{standard}")).StatusCode);
        var update = Draft(standard, new("daily", null, ["09:00", "17:00"], null)) with { PlanNo = plan.PlanNo, AssigneeKey = "dev-ipqa-2" };
        var response = await app.Client.PutAsJsonAsync($"/api/patrol-plans/{plan.Id}", update);
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, await app.GenerateAsync()); // edited schedule cannot reinterpret earlier day
        using var afterScope = app.Factory.Services.CreateScope();
        var after = await afterScope.ServiceProvider.GetRequiredService<IpqcDbContext>().PatrolTasks.AsNoTracking().ToListAsync();
        Assert.Equal(4, after.Count);
        Assert.All(after, x => Assert.Equal("dev-ipqa-1", x.AssignedInspectorKey));
        Assert.All(after, x => Assert.Equal(standard, x.PatrolStandardId));
    }

    [Fact]
    public async Task Disabled_time_is_not_backfilled_and_catchup_is_bounded()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var plan = await app.CreateAsync(Draft(standard));
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(3, await app.GenerateAsync());
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsync($"/api/patrol-plans/{plan.Id}/disable", null)).StatusCode);
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, await app.GenerateAsync());
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsync($"/api/patrol-plans/{plan.Id}/enable", null)).StatusCode);
        Assert.Equal(0, await app.GenerateAsync());
        app.Clock.Now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, await app.GenerateAsync());
        app.Clock.Now = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(13, await app.GenerateAsync()); // only the last 24 hours, inclusive
    }

    [Fact]
    public async Task Database_enforces_unique_plan_occurrence()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var plan = await app.CreateAsync(Draft(standard));
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, await app.GenerateAsync());
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var original = await db.PatrolTasks.AsNoTracking().OrderBy(x => x.ScheduledOccurrenceUtc).FirstAsync();
        db.PatrolTasks.Add(new PatrolTask { Id = Guid.NewGuid(), TaskNo = $"PT-{Guid.NewGuid():N}", PatrolPlanId = plan.Id,
            PatrolStandardId = standard, AssignedInspectorKey = "dev-ipqa-1", ScheduledOccurrenceUtc = original.ScheduledOccurrenceUtc,
            GeneratedAtUtc = app.Clock.Now.UtcDateTime, CreatedAtUtc = app.Clock.Now.UtcDateTime, UpdatedAtUtc = app.Clock.Now.UtcDateTime });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Overlapping_generation_passes_produce_one_task_per_occurrence()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        await app.CreateAsync(Draft(standard));
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => app.GenerateAsync()));
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.Equal(2, await db.PatrolTasks.CountAsync());
        Assert.Equal(0, await app.GenerateAsync());
    }

    [Fact]
    public async Task Effective_end_and_standard_change_apply_only_to_future_tasks()
    {
        await using var app = await TestApp.CreateAsync();
        var firstStandard = await app.StandardAsync();
        var secondStandard = await app.StandardAsync();
        var plan = await app.CreateAsync(Draft(firstStandard) with { EffectiveEndLocal = "2026-10-03T10:00" });
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, await app.GenerateAsync()); // inclusive 08:00 and 10:00
        var changed = Draft(secondStandard) with { PlanNo = plan.PlanNo, AssigneeKey = "dev-ipqa-2" };
        var update = await app.Client.PutAsJsonAsync($"/api/patrol-plans/{plan.Id}", changed);
        update.EnsureSuccessStatusCode();
        app.Clock.Now = new DateTimeOffset(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, await app.GenerateAsync());
        using var scope = app.Factory.Services.CreateScope();
        var tasks = await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().PatrolTasks.AsNoTracking().OrderBy(x => x.ScheduledOccurrenceUtc).ToListAsync();
        Assert.Equal([firstStandard, firstStandard, secondStandard], tasks.Select(x => x.PatrolStandardId).ToArray());
        Assert.Equal(["dev-ipqa-1", "dev-ipqa-1", "dev-ipqa-2"], tasks.Select(x => x.AssignedInspectorKey).ToArray());
    }

    [Fact]
    public async Task Disabled_plan_without_assignee_cannot_be_enabled()
    {
        await using var app = await TestApp.CreateAsync();
        var standard = await app.StandardAsync();
        var plan = await app.CreateAsync(Draft(standard, enabled: false) with { AssigneeKey = null });
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsync($"/api/patrol-plans/{plan.Id}/enable", null)).StatusCode);
        Assert.Equal(0, await app.GenerateAsync());
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(InitialUtc);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestApp(WebApplicationFactory<Program> factory, HttpClient client, ManualClock clock, string dbPath) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public ManualClock Clock { get; } = clock;

        public static async Task<TestApp> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-plan-{Guid.NewGuid():N}.db");
            var clock = new ManualClock();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureLogging(logging => logging.ClearProviders());
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
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
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Dev-Role", "admin");
            return new TestApp(factory, client, clock, path);
        }

        public async Task<Guid> StandardAsync()
        {
            var response = await Client.PostAsJsonAsync("/api/patrol-standards", new StandardInput("Standard", null, null, null, null, "Line", null, []));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StandardOutput>())!.Id;
        }
        public async Task<PlanOutput> CreateAsync(PlanInput input)
        {
            var response = await Client.PostAsJsonAsync("/api/patrol-plans", input);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PlanOutput>())!;
        }
        public async Task<int> GenerateAsync()
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>().GenerateDueAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
