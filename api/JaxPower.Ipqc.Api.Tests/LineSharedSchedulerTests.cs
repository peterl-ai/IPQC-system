using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedSchedulerTests
{
    [Fact]
    public async Task R03_11_13_Two_lines_four_tasks_daily_and_restart_is_idempotent()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); await Plan(f, s, f.Line2);
        f.Clock.Now = f.Clock.Now.AddHours(24); Assert.Equal(4, await Generate(f)); Assert.Equal(0, await Generate(f));
        var tasks = await Tasks(f); Assert.Equal(4, tasks.Count);
        Assert.All(tasks, t => { Assert.Null(t.AssignedInspectorUserId); Assert.Equal("", t.AssignedInspectorKey); Assert.Null(t.Shift);
            Assert.Equal(0, t.CurrentRevisionNo); Assert.Equal("PendingInspection", t.Status); Assert.Equal("ValidV1", PlanningSnapshot.Status(t));
            Assert.Equal(0, t.ScheduledOccurrenceUtc.Ticks % 10); Assert.All(t.Items, i => Assert.Null(i.IsNa)); });
        Assert.Equal(2, tasks.Count(x => PlanningSnapshot.Read(x)!.Round == "Day"));
        Assert.Equal(2, tasks.Count(x => PlanningSnapshot.Read(x)!.Round == "Night"));
    }
    [Theory]
    [InlineData(2026, 3, 8, 11, -240)][InlineData(2026, 11, 1, 12, -300)]
    public async Task R03_12_Dst_day_round_freezes_calendar_and_utc_offset(int year, int month, int day, int hour, int offset)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(year, month, day, hour, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); Assert.Equal(1, await Generate(f));
        var frozen = PlanningSnapshot.Read(Assert.Single(await Tasks(f)))!;
        Assert.Equal(offset, frozen.UtcOffsetMinutes); Assert.Equal("Day", frozen.Round); Assert.Equal(f.Clock.Now.UtcDateTime, frozen.ScheduledOccurrenceUtc);
    }
    [Fact]
    public async Task R03_05_14_Creation_instant_microseconds_future_and_disabled_boundaries()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); Assert.Equal(1, await Generate(f));
        Assert.Equal(f.Clock.Now.UtcDateTime.AddTicks(-10), p.GenerationNotBeforeUtc);
        p = await Toggle(f, p, false); f.Clock.Now = f.Clock.Now.AddDays(2);
        Assert.Equal(0, await Generate(f)); p = await Toggle(f, p, true); Assert.Equal(0, await Generate(f));
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await Generate(f));
        Assert.Equal(2, (await Tasks(f)).Count);
    }
    [Fact]
    public async Task R03_13_14_Bad_and_unadmitted_plans_are_isolated_catchup_is_bounded()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); var p = await Plan(f, s); var good = await Plan(f, s, f.Line2);
        await f.Edit(async db => (await db.PatrolPlans.SingleAsync(x => x.Id == p.Id)).ScheduleDefinition = "broken");
        f.Clock.Now = f.Clock.Now.AddDays(10); Assert.Equal(2, await Generate(f));
        await f.Edit(async db => (await db.PatrolPlans.SingleAsync(x => x.Id == good.Id)).SharedPlanRevision = null);
        f.Clock.Now = f.Clock.Now.AddDays(1); Assert.Equal(0, await Generate(f));
    }
    [Fact]
    public async Task R03_16_Cross_plan_winner_and_historical_collision_do_not_create_competing_tasks()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); Assert.Equal(1, await Generate(f));
        await Toggle(f, p, false); var replacement = await Plan(f, s); Assert.Equal(0, await Generate(f));
        await Toggle(f, replacement, false);
        // Separate historical task at the next occurrence: no conversion/adoption.
        f.Clock.Now = f.Clock.Now.AddHours(12); var history = await f.Task();
        var third = await Plan(f, s); Assert.Equal(0, await Generate(f));
        var preserved = (await Tasks(f)).Single(x => x.Id == history); Assert.Null(preserved.PlanningSnapshotVersion);
        Assert.Equal(2, (await Tasks(f)).Count); Assert.NotEqual(third.Id, preserved.PatrolPlanId);
    }
    [Fact]
    public async Task R03_12_Stored_calendar_key_blocks_timezone_rule_remapping_without_rewrite()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); await Plan(f, s); await Generate(f);
        // Emulate an older timezone database with a different UTC mapping, preserving coherent frozen offset facts.
        await f.Edit(async db =>
        {
            var task = await db.PatrolTasks.Include(x => x.Items).SingleAsync(); var snapshot = PlanningSnapshot.Read(task)!;
            task.ScheduledOccurrenceUtc = task.ScheduledOccurrenceUtc.AddHours(1);
            task.PlanningSnapshotJson = PlanningSnapshot.Serialize(snapshot with { ScheduledOccurrenceUtc = task.ScheduledOccurrenceUtc, UtcOffsetMinutes = -360 });
        });
        var before = (await Tasks(f)).Single().PlanningSnapshotJson;
        Assert.Equal(0, await Generate(f)); Assert.Equal(before, (await Tasks(f)).Single().PlanningSnapshotJson);
    }
    [Theory]
    [InlineData("2026-03-07T12:00:00Z", "Day")][InlineData("2026-03-08T00:00:00Z", "Night")]
    [InlineData("2026-03-08T11:00:00Z", "Day")][InlineData("2026-03-08T23:00:00Z", "Night")]
    [InlineData("2026-10-31T11:00:00Z", "Day")][InlineData("2026-10-31T23:00:00Z", "Night")]
    [InlineData("2026-11-01T12:00:00Z", "Day")][InlineData("2026-11-02T00:00:00Z", "Night")]
    public void R03_12_All_eight_day_and_night_dst_vectors_use_new_york(string text, string round)
    {
        var utc = DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var schedule = new ScheduleInput("daily", null, ["07:00", "19:00"], null);
        Assert.Equal(utc, Assert.Single(PatrolSchedule.Occurrences(schedule, PatrolSchedule.PlantTimeZone, utc.AddDays(-1), utc, utc)));
        Assert.Equal(round == "Day" ? 7 : 19, TimeZoneInfo.ConvertTimeFromUtc(utc, LineSharedPlanService.Zone).Hour);
    }
    [Theory]
    [InlineData(0, 1)][InlineData(1000, 14)]
    public async Task R03_13_Configured_catchup_window_clamps_to_one_and_168_hours(int setting, int expected)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var s = await Standard(f); await Plan(f, s);
        var configuration = f.Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration["Scheduler:CatchUpWindowHours"] = setting.ToString(System.Globalization.CultureInfo.InvariantCulture);
        f.Clock.Now = f.Clock.Now.AddDays(10);
        if (setting == 0) f.Clock.Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, await Generate(f)); Assert.Equal(0, await Generate(f));
    }
}
