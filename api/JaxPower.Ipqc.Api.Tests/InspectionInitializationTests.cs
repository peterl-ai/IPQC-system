using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class InspectionInitializationTests
{
    internal const string Inventory = "/api/admin/inspection-identities";
    internal static string Initialize(Guid task) => $"{Inventory}/{task}/initialize";
    internal static async Task<InspectionIdentityInitializeInput> Input(PersonalSessionFixture f, HttpClient admin, Guid task)
    {
        var response = await admin.GetAsync(Inventory + "?pageSize=100"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<InspectionIdentityInventory>())!.Items, x => x.TaskId == task);
        return new(f.Line1, 2, PersonalSessionFixture.Environment, row.PlanningSnapshotHash!, "Synthetic explicit maintenance reason");
    }

    [Fact] public async Task ID03_Explicit_Admin_initialization_and_concurrent_retries()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await InitializeRace(f); }

    internal static async Task InitializeRace(PersonalSessionFixture f)
    {
        var task = await InspectionIdentityTests.Existing(f); var auth = await f.Login(); var adminAuth = await f.Login("admin");
        var client = f.Client(auth.AccessToken); var admin = f.Client(adminAuth.AccessToken);
        var input = await Input(f, admin, task);
        var before = await InspectionIdentityMigrationTests.OldFacts(f);
        var uninitialized = (await client.GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!;
        Assert.Equal("Uninitialized", uninitialized.State); Assert.Empty(uninitialized.Items);
        Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f));
        f.Clock.Now = f.Clock.Now.AddMinutes(3);
        f.Fault.Arm("INSERT INTO \"InspectionAttempts\"");
        var first = admin.PostAsJsonAsync(Initialize(task), input);
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var second = admin.PostAsJsonAsync(Initialize(task), input);
        f.Fault.Release.TrySetResult();
        var responses = await Task.WhenAll(first, second); var ids = new List<Guid[]>();
        foreach (var response in responses) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.Content.ReadFromJsonAsync<InspectionIdentityOutput>())!;
            Assert.Equal("Initialized", data.State); Assert.False(data.CanExecute); Assert.Equal(2, data.Items.Count);
            ids.Add(data.Items.Select(x => x.InspectionAttemptId).ToArray());
            Assert.All(data.Items, x => { Assert.Equal("ExplicitInitialization", x.CreationSource); Assert.Equal(f.Clock.Now.UtcDateTime, x.CreatedAtUtc); });
        }
        Assert.Equal(ids[0], ids[1]);
        var retry = await admin.PostAsJsonAsync(Initialize(task), input); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(ids[0], (await retry.Content.ReadFromJsonAsync<InspectionIdentityOutput>())!.Items.Select(x => x.InspectionAttemptId));
        Assert.Equal(2, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.InspectionAttemptApplicabilities.CountAsync()));
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f));
        var audits = await f.Read(db => db.InspectionAttempts.AsNoTracking().ToListAsync());
        Assert.Single(audits.Select(x => x.CreationEventId).Distinct());
        Assert.All(audits, x => { Assert.Equal(TestAccounts.AdminId, x.CreatedByUserId); Assert.Equal("admin", x.CreatedByUsername);
            Assert.Equal(adminAuth.PersonalSessionId, x.CreatedByPersonalSessionId); Assert.Equal(input.Reason, x.InitializationReason); });
        var inventory = (await admin.GetFromJsonAsync<InspectionIdentityInventory>(Inventory))!;
        Assert.Equal("AlreadyInitialized", Assert.Single(inventory.Items).Category);
        var issued = await WorkContextFixture.Issue(f, task, auth);
        var preflight = await client.PostAsJsonAsync(WorkContextFixture.Root(task) + "/validate-sources", WorkContextFixture.Sources(f, issued));
        Assert.Equal(HttpStatusCode.OK, preflight.StatusCode); Assert.False((await preflight.Content.ReadFromJsonAsync<WorkContextPreflight>())!.AcceptsWork);
        Assert.Equal(0, await f.Read(db => db.PatrolTaskItemSamples.CountAsync()));
    }

    [Theory][InlineData("historical")][InlineData("invalid")][InlineData("answers")][InlineData("samples")]
    [InlineData("submission")][InlineData("review")][InlineData("legacy")][InlineData("partial")][InlineData("minimum")]
    public async Task ID03_Unsupported_inventory_never_repairs_or_creates_history(string category)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Unsupported(f, category); }

    internal static async Task Unsupported(PersonalSessionFixture f, string kind)
    {
        var task = await InspectionIdentityTests.Existing(f); var admin = f.Client((await f.Login("admin")).AccessToken);
        await f.Edit(async db => {
            var t = await db.PatrolTasks.Include(x => x.Items).SingleAsync(x => x.Id == task);
            switch (kind) {
                case "historical": t.PlanningSnapshotVersion = null; t.PlanningSnapshotJson = null; break;
                case "invalid": t.PlanningSnapshotJson = "{}"; break;
                case "answers": t.Items[0].Remarks = "Synthetic legacy answer"; break;
                case "samples": db.PatrolTaskItemSamples.Add(new() { Id = Guid.NewGuid(), PatrolTaskItemId = t.Items[0].Id, SequenceNo = 1, InspectionValue = 2.5m }); break;
                case "submission": db.PatrolTaskSubmissions.Add(new() { Id = Guid.NewGuid(), PatrolTaskId = task, RevisionNo = 1, SubmittedAtUtc = f.Clock.Now.UtcDateTime, SubmittedBy = "synthetic" }); break;
                case "review": db.PatrolTaskReviews.Add(new() { Id = Guid.NewGuid(), PatrolTaskId = task, RevisionNo = 1, ReviewedAtUtc = f.Clock.Now.UtcDateTime, Reviewer = "synthetic", Decision = "Rejected", Reason = "Synthetic historical reason" }); break;
                case "legacy": t.PlanningSnapshotVersion = null; t.PlanningSnapshotJson = null; t.WorkflowVersion = WorkflowVersion.LegacyPersonalV1; break;
                case "partial": db.InspectionAttempts.Add(new() { Id = Guid.NewGuid(), PatrolTaskItemId = t.Items[0].Id,
                    CreationEventId = Guid.NewGuid(), CreationSource = "TaskGeneration", CreatedAtUtc = f.Clock.Now.UtcDateTime, MinimumSampleCount = 1 }); break;
                case "minimum": var frozen = PlanningSnapshot.Read(t)!; foreach (var i in t.Items) i.SampleCount = "51";
                    t.PlanningSnapshotJson = PlanningSnapshot.Serialize(frozen with { Items = frozen.Items.Select(x => x with { SampleCount = "51" }).ToList() }); break;
            }
        });
        var inventory = (await admin.GetFromJsonAsync<InspectionIdentityInventory>(Inventory))!;
        var row = Assert.Single(inventory.Items);
        Assert.Equal(kind switch { "historical" => "HistoricalUnadmitted", "invalid" => "InvalidPlanningSnapshot",
            "legacy" => "LegacyPersonalV1", "partial" => "UnsupportedIdentity", "minimum" => "UnsupportedRequirements", _ => "HistoricalInspectionData" }, row.Category);
        var count = await f.Read(db => db.InspectionAttempts.CountAsync()); var before = await InspectionIdentityMigrationTests.OldFacts(f);
        var r = await admin.PostAsJsonAsync(Initialize(task), new InspectionIdentityInitializeInput(f.Line1, 2,
            PersonalSessionFixture.Environment, row.PlanningSnapshotHash ?? new string('0', 64), "Synthetic rejection test"));
        Assert.Equal(kind == "legacy" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(count, await f.Read(db => db.InspectionAttempts.CountAsync()));
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f));
    }

    [Fact] public async Task ID03_Roles_family_shape_stale_inventory_and_direct_service_guards()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }

    internal static async Task Authority(PersonalSessionFixture f)
    {
        var task = await InspectionIdentityTests.Existing(f); var auth = await f.Login("admin"); var admin = f.Client(auth.AccessToken);
        var input = await Input(f, admin, task);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync(Initialize(task), input)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Anonymous.PostAsync(Initialize(task),new StringContent("{broken",System.Text.Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(Initialize(task),new {
            input.ExpectedTaskLineId,input.ExpectedWorkflowVersion,input.ExpectedEnvironmentId,input.ExpectedPlanningSnapshotHash,input.Reason,
            accepted=true })).StatusCode);
        foreach (var name in new[] { "dev-ipqa-1", "pqe" }) {
            var c = f.Client((await f.Login(name)).AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Initialize(task), input)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Inventory)).StatusCode);
        }
        var noFamily = f.Token(auth, x => x.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(noFamily).PostAsJsonAsync(Initialize(task), input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Initialize(task), input with { Reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Initialize(task), input with { ExpectedPlanningSnapshotHash = new string('0',64) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Initialize(task), input with { ExpectedTaskLineId = f.Line2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Initialize(task), input with { ExpectedEnvironmentId = "other" })).StatusCode);
        using var scope = f.Factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<InspectionAttemptService>();
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(auth, "Admin").Claims));
        async Task Deny(Func<Task> action, int status) { var error = await Assert.ThrowsAsync<PersonalAccessException>(action); Assert.Equal(status, error.Status); }
        await Deny(async () => await service.InitializeAsync(task, input, unauthenticated, default), 401);
        await Deny(async () => await service.InventoryAsync(1,20,unauthenticated,default),401);
        await Deny(async () => await service.ReadAsync(task,null,unauthenticated,default),401);
        var ipqa = await f.Login();
        await Deny(async () => await service.InitializeAsync(task,input,WorkContextFixture.Principal(ipqa),default),403);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.AdminId)).MustChangePassword = true);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(Initialize(task),input)).StatusCode);
        Assert.Equal(0, await f.Read(db => db.InspectionAttempts.CountAsync()));
    }

    [Theory][InlineData("attempt")][InlineData("applicability")][InlineData("commit")]
    public async Task ID03_Failed_initialization_rolls_back_audit_and_identity(string point)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Rollback(f, point); }

    internal static async Task Rollback(PersonalSessionFixture f, string point)
    {
        var task = await InspectionIdentityTests.Existing(f); var admin = f.Client((await f.Login("admin")).AccessToken); var input = await Input(f,admin,task);
        if (point == "commit") f.CommitFault.Armed = true;
        else f.Fault.Arm(point == "attempt" ? "INSERT INTO \"InspectionAttempts\"" : "INSERT INTO \"InspectionAttemptApplicabilities\"", true);
        var r = await admin.PostAsJsonAsync(Initialize(task),input); Assert.Equal(HttpStatusCode.InternalServerError,r.StatusCode);
        Assert.Equal(0,await f.Read(db=>db.InspectionAttempts.CountAsync()));
        Assert.Equal(0,await f.Read(db=>db.InspectionAttemptApplicabilities.CountAsync()));
        var retry = await admin.PostAsJsonAsync(Initialize(task),input); Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
        Assert.Equal(2,await f.Read(db=>db.InspectionAttempts.CountAsync()));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task ID03_Line_change_and_initializer_lock_order(bool lineFirst)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await LineRace(f,lineFirst); }
    internal static async Task LineRace(PersonalSessionFixture f, bool lineFirst)
    {
        var task = await InspectionIdentityTests.Existing(f); var admin = f.Client((await f.Login("admin")).AccessToken);
        var input = await Input(f,admin,task);
        f.Fault.Arm(lineFirst ? "UPDATE \"ProductionLines\"" : "INSERT INTO \"InspectionAttempts\"");
        var initialize = lineFirst ? null : admin.PostAsJsonAsync(Initialize(task),input);
        var line = lineFirst ? admin.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate",new ProvisioningRevisionInput(1)) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        initialize ??= admin.PostAsJsonAsync(Initialize(task),input);
        line ??= admin.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate",new ProvisioningRevisionInput(1));
        f.Fault.Release.TrySetResult(); var response = await initialize;
        Assert.Equal(HttpStatusCode.OK,(await line).StatusCode);
        Assert.False(await f.Read(db=>db.ProductionLines.Where(x=>x.Id==f.Line1).Select(x=>x.IsActive).SingleAsync()));
        Assert.Equal(lineFirst ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(lineFirst ? 0 : 2,await f.Read(db=>db.InspectionAttempts.CountAsync()));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task ID03_Current_Admin_family_closure_serializes_with_initialization(bool logoutFirst)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await LogoutRace(f,logoutFirst); }
    internal static async Task LogoutRace(PersonalSessionFixture f, bool logoutFirst)
    {
        var task = await InspectionIdentityTests.Existing(f); var auth = await f.Login("admin"); var admin = f.Client(auth.AccessToken);
        var input = await Input(f,admin,task);
        var otherFamilies = await f.Read(db=>db.RefreshTokenSessions.CountAsync(x=>x.UserId==TestAccounts.AdminId && x.PersonalSessionId!=auth.PersonalSessionId && x.RevokedAtUtc==null));
        f.Fault.Arm(logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"InspectionAttempts\"");
        var initialize = logoutFirst ? null : admin.PostAsJsonAsync(Initialize(task),input);
        var logout = logoutFirst ? admin.PostAsJsonAsync("/api/auth/logout",new RefreshInput(auth.RefreshToken)) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        initialize ??= admin.PostAsJsonAsync(Initialize(task),input);
        logout ??= admin.PostAsJsonAsync("/api/auth/logout",new RefreshInput(auth.RefreshToken));
        f.Fault.Release.TrySetResult(); var response = await initialize;
        Assert.Equal(HttpStatusCode.NoContent,(await logout).StatusCode);
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(logoutFirst ? 0 : 2,await f.Read(db=>db.InspectionAttempts.CountAsync()));
        Assert.Equal(0,await f.Read(db=>db.RefreshTokenSessions.CountAsync(x=>x.UserId==TestAccounts.AdminId && x.PersonalSessionId==auth.PersonalSessionId && x.RevokedAtUtc==null)));
        Assert.Equal(otherFamilies,await f.Read(db=>db.RefreshTokenSessions.CountAsync(x=>x.UserId==TestAccounts.AdminId && x.PersonalSessionId!=auth.PersonalSessionId && x.RevokedAtUtc==null)));
    }
}
