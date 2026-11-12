using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class InspectionIdentityTests
{
    internal static string Root(Guid task) => $"/api/line-patrol/tasks/{task}/inspection-attempts";

    // Creates an actual admitted pre-identity fixture using persisted planning sources,
    // also usable while the database is still at the merged R3204 migration.
    internal static async Task<Guid> Existing(PersonalSessionFixture f, string sampleCount = "")
    {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero);
        var standard = await LineSharedPlanningFixture.Standard(f);
        await f.Edit(async db => { foreach (var i in await db.PatrolStandardItems.Where(x => x.PatrolStandardId == standard).ToListAsync()) i.SampleCount = sampleCount; });
        var plan = await LineSharedPlanningFixture.Plan(f, standard); f.Clock.Now = f.Clock.Now.AddMinutes(2);
        var id = Guid.NewGuid();
        await f.Edit(async db => {
            var p = await db.PatrolPlans.SingleAsync(x => x.Id == plan.Id);
            var s = await db.PatrolStandards.Include(x => x.InspectionItems).SingleAsync(x => x.Id == standard);
            var line = await db.ProductionLines.SingleAsync(x => x.Id == f.Line1);
            var t = new PatrolTask { Id = id, TaskNo = "SYN-OLD-" + id, WorkflowVersion = WorkflowVersion.LineSharedV1,
                LineId = line.Id, PatrolPlanId = p.Id, PatrolStandardId = s.Id, PlanningSnapshotVersion = 1,
                ScheduledOccurrenceUtc = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc), GeneratedAtUtc = f.Clock.Now.UtcDateTime,
                CreatedAtUtc = f.Clock.Now.UtcDateTime, UpdatedAtUtc = f.Clock.Now.UtcDateTime,
                PlanNoSnapshot = p.PlanNo, PlanNameSnapshot = p.PlanName, StandardNameSnapshot = s.PatrolStandardName,
                FactoryCodeSnapshot = s.FactoryCode, FactoryNameSnapshot = s.FactoryName, WorkshopCodeSnapshot = s.WorkshopCode,
                LineCodeSnapshot = line.Code, LineNameSnapshot = line.Name, MaterialCodeSnapshot = s.MaterialCode,
                SourceStandardUpdatedAtUtc = s.UpdatedAtUtc,
                Items = s.InspectionItems.Select(x => new PatrolTaskItem { Id = Guid.NewGuid(),
                    SourcePatrolStandardItemId = x.Id, SequenceNo = x.SequenceNo, InspectionItem = x.InspectionItem,
                    InspectionType = x.InspectionType, SampleCount = x.SampleCount, PhotoRequirement = x.PhotoRequirement }).ToList() };
            t.PlanningSnapshotJson = PlanningSnapshot.Serialize(PlanningSnapshot.Freeze(t, p, s, line)); db.PatrolTasks.Add(t);
        });
        return id;
    }

    [Fact] public async Task ID01_Real_identity_read_scope_and_no_fake_work()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Identity(f); }

    internal static async Task Identity(PersonalSessionFixture f)
    {
        var task = await WorkContextFixture.Task(f); var auth = await f.Login(); var client = f.Client(auth.AccessToken);
        var before = await f.Read(async db => new { Attempts = await db.InspectionAttempts.CountAsync(),
            Contexts = await db.WorkContexts.CountAsync(), Task = await db.PatrolTasks.AsNoTracking().SingleAsync(x => x.Id == task) });
        var r = await client.GetAsync(Root(task)); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        var data = (await r.Content.ReadFromJsonAsync<InspectionIdentityOutput>())!;
        Assert.Equal("Initialized", data.State); Assert.False(data.CanExecute); Assert.Equal(2, data.Items.Count);
        var definitions = (await LineSharedPlanningFixture.Tasks(f)).Single().Items;
        foreach (var a in data.Items)
        {
            var item = definitions.Single(x => x.Id == a.TaskItemId);
            Assert.NotEqual(Guid.Empty, a.InspectionAttemptId); Assert.NotEqual(item.Id, a.InspectionAttemptId);
            Assert.NotEqual(item.SourcePatrolStandardItemId, a.InspectionAttemptId);
            Assert.Equal(task, a.TaskId); Assert.Equal(f.Line1, a.LineId); Assert.Equal(2, a.WorkflowVersion);
            Assert.Equal("Initial", a.Kind); Assert.Equal("TaskGeneration", a.CreationSource);
            Assert.Equal(before.Task.GeneratedAtUtc, a.CreatedAtUtc); Assert.Equal(1, a.MinimumSampleCount); Assert.True(a.IsCurrentApplicable);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Root(task) + "/" + a.InspectionAttemptId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(task) + "/" + item.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(task) + "/" + item.SourcePatrolStandardItemId)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(task) + "/" + Guid.NewGuid())).StatusCode);
        var other = await f.Task();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(other) + "/" + data.Items[0].InspectionAttemptId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(Root(task) + "?expectedTaskLineId=" + f.Line2)).StatusCode);
        var again = (await client.GetFromJsonAsync<InspectionIdentityOutput>(Root(task)))!;
        Assert.Equal(data.Items, again.Items);
        await WorkContextFixture.Issue(f, task, auth);
        Assert.Equal(before.Attempts, await f.Read(db => db.InspectionAttempts.CountAsync()));
        await f.Read(async db => {
            var t = await db.PatrolTasks.SingleAsync(x => x.Id == task);
            Assert.Equal(before.Task.Status, t.Status); Assert.Null(t.StartedAtUtc); Assert.Equal(0, t.CurrentRevisionNo);
            Assert.Equal(0, await db.PatrolTaskItemSamples.CountAsync()); Assert.Equal(0, await db.PatrolTaskSubmissions.CountAsync());
            Assert.Equal(0, await db.PatrolTaskReviews.CountAsync()); return true;
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(Root(task))).StatusCode);
        foreach (var role in new[] { "admin", "pqe" })
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Client((await f.Login(role)).AccessToken).GetAsync(Root(task))).StatusCode);
    }

    [Fact] public async Task ID02_Concurrent_and_duplicate_generation_are_atomic()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Generation(f); }

    internal static async Task Generation(PersonalSessionFixture f)
    {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero);
        await LineSharedPlanningFixture.Plan(f, await LineSharedPlanningFixture.Standard(f));
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var counts = await Task.WhenAll(LineSharedPlanningFixture.Generate(f), LineSharedPlanningFixture.Generate(f));
        Assert.Equal(1, counts.Sum()); Assert.Equal(0, await LineSharedPlanningFixture.Generate(f));
        Assert.Equal(1, await f.Read(db => db.PatrolTasks.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.PatrolTaskItems.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.InspectionAttemptApplicabilities.CountAsync()));
    }

    [Theory][InlineData("INSERT INTO \"InspectionAttempts\"")][InlineData("INSERT INTO \"InspectionAttemptApplicabilities\"")]
    public async Task ID02_Identity_failure_rolls_back_generation(string fragment)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await GenerationRollback(f, fragment); }

    internal static async Task GenerationRollback(PersonalSessionFixture f, string fragment)
    {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero);
        await LineSharedPlanningFixture.Plan(f, await LineSharedPlanningFixture.Standard(f)); f.Clock.Now = f.Clock.Now.AddMinutes(1);
        f.Fault.Arm(fragment, true); Assert.Equal(0, await LineSharedPlanningFixture.Generate(f));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await f.Read(db => db.PatrolTasks.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskItems.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.InspectionAttemptApplicabilities.CountAsync()));
        Assert.Equal(1, await LineSharedPlanningFixture.Generate(f));
    }

    [Fact] public async Task ID02_Precommit_failure_rolls_back_all_generated_identities()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await GenerationCommitRollback(f); }
    internal static async Task GenerationCommitRollback(PersonalSessionFixture f)
    {
        f.Clock.Now = new(2026,11,11,11,59,0,TimeSpan.Zero);
        await LineSharedPlanningFixture.Plan(f,await LineSharedPlanningFixture.Standard(f)); f.Clock.Now = f.Clock.Now.AddMinutes(1);
        f.CommitFault.FailTaskCommit = true; Assert.Equal(0,await LineSharedPlanningFixture.Generate(f));
        Assert.False(f.CommitFault.FailTaskCommit);
        Assert.Equal(0,await f.Read(db=>db.PatrolTasks.CountAsync())); Assert.Equal(0,await f.Read(db=>db.PatrolTaskItems.CountAsync()));
        Assert.Equal(0,await f.Read(db=>db.InspectionAttempts.CountAsync())); Assert.Equal(0,await f.Read(db=>db.InspectionAttemptApplicabilities.CountAsync()));
        Assert.Equal(1,await LineSharedPlanningFixture.Generate(f));
    }

    [Theory][InlineData("", 1)][InlineData("1", 1)][InlineData("50", 50)][InlineData("51", 0)]
    [InlineData("0", 0)][InlineData("-1", 0)][InlineData("1.5", 0)][InlineData("invalid", 0)]
    public async Task Frozen_minimum_is_defaulted_only_if_missing_and_never_clamped(string value, int expected)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var id = await Existing(f);
        await f.Edit(async db => {
            var t = await db.PatrolTasks.Include(x => x.Items).SingleAsync(x => x.Id == id);
            var frozen = PlanningSnapshot.Read(t)!;
            foreach (var i in t.Items) i.SampleCount = value;
            t.PlanningSnapshotJson = PlanningSnapshot.Serialize(frozen with { Items = frozen.Items.Select(x => x with { SampleCount = value }).ToList() });
        });
        var admin = f.Client((await f.Login("admin")).AccessToken);
        var initialize = await admin.PostAsJsonAsync(InspectionInitializationTests.Initialize(id),await InspectionInitializationTests.Input(f,admin,id));
        Assert.Equal(expected != 0 ? HttpStatusCode.OK : HttpStatusCode.Conflict,initialize.StatusCode);
        var read = (await f.Client((await f.Login()).AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(Root(id)))!;
        Assert.Equal(expected != 0 ? "Initialized" : "UnsupportedRequirements", read.State);
        if (expected != 0) Assert.All(read.Items, x => { Assert.Equal(expected, x.MinimumSampleCount); Assert.Equal(value, x.FrozenSampleCount); });
        else { Assert.Empty(read.Items); Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync())); }
    }

    [Fact] public async Task ID04_Uniqueness_scope_restriction_and_immutable_history()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Constraints(f); }

    internal static async Task Constraints(PersonalSessionFixture f)
    {
        var task = await WorkContextFixture.Task(f);
        var a = await f.Read(db => db.InspectionAttempts.AsNoTracking().FirstAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db => {
            db.InspectionAttempts.Add(new InspectionAttempt { Id = Guid.NewGuid(), PatrolTaskItemId = a.PatrolTaskItemId,
                CreationEventId = Guid.NewGuid(), CreationSource = "TaskGeneration", CreatedAtUtc = f.Clock.Now.UtcDateTime,
                MinimumSampleCount = 1 }); return Task.CompletedTask;
        }));
        foreach (var minimum in new[] { 0,51 }) {
            var check = await Assert.ThrowsAsync<DbUpdateException>(()=>f.Edit(db=> {
                db.InspectionAttempts.Add(new() { Id=Guid.NewGuid(),PatrolTaskItemId=a.PatrolTaskItemId,
                    CreationEventId=Guid.NewGuid(),CreationSource="TaskGeneration",CreatedAtUtc=f.Clock.Now.UtcDateTime,MinimumSampleCount=minimum });
                return Task.CompletedTask;
            }));
            if (f.PostgresRoot is null) Assert.Equal(275,((Microsoft.Data.Sqlite.SqliteException)check.InnerException!).SqliteExtendedErrorCode);
            else Assert.Equal("23514",((Npgsql.PostgresException)check.InnerException!).SqlState);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.InspectionAttempts.FirstAsync()).MinimumSampleCount = 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => db.InspectionAttempts.Remove(await db.InspectionAttempts.FirstAsync())));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Edit(async db => await db.PatrolTaskItems.Where(x => x.Id == a.PatrolTaskItemId).ExecuteDeleteAsync()));
        var otherItem = await f.Read(db => db.PatrolTaskItems.Where(x => x.PatrolTaskId == task && x.Id != a.PatrolTaskItemId).Select(x => x.Id).SingleAsync());
        // Remove both bindings only inside a rollback-only transaction so uniqueness cannot
        // mask the joint-scope FK oracle. Existing attempt IDs alone must not suffice.
        var scopeError = await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.InspectionAttemptApplicabilities.ExecuteDeleteAsync();
            db.InspectionAttemptApplicabilities.Add(new() { PatrolTaskItemId = otherItem, InspectionAttemptId = a.Id });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }));
        if (f.PostgresRoot is null) Assert.Equal(787, ((Microsoft.Data.Sqlite.SqliteException)scopeError.InnerException!).SqliteExtendedErrorCode);
        else Assert.Equal("23503", ((Npgsql.PostgresException)scopeError.InnerException!).SqlState);
        Assert.Equal(2, await f.Read(db => db.InspectionAttemptApplicabilities.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.InspectionAttempts.CountAsync()));
    }
}
