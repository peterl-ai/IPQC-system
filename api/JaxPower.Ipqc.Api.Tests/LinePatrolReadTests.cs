using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LinePatrolReadTests
{
    [Fact]
    public async Task Retired_reservations_revoked_and_replaced_device_metadata_cannot_restrict_personal_reads()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var id = await f.Task(); var now = f.Clock.Now.UtcDateTime;
        await f.Edit(db =>
        {
            var reserved = new RegisteredDevice { Id = Guid.NewGuid(), DisplayName = "Synthetic reserved", LineId = f.Line1,
                CreatedByUserId = TestAccounts.AdminId, UpdatedByUserId = TestAccounts.AdminId, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.RegisteredDevices.AddRange(reserved, new RegisteredDevice { Id = Guid.NewGuid(), DisplayName = "Synthetic revoked",
                Status = DeviceProvisioningStatus.Revoked, TerminalAtUtc = now, TerminalReason = "Synthetic test closure",
                CreatedByUserId = TestAccounts.AdminId, UpdatedByUserId = TestAccounts.AdminId, CreatedAtUtc = now, UpdatedAtUtc = now },
                new RegisteredDevice { Id = Guid.NewGuid(), DisplayName = "Synthetic replaced", Status = DeviceProvisioningStatus.Replaced,
                    ReplacedByDeviceId = reserved.Id, TerminalAtUtc = now, TerminalReason = "Synthetic test replacement",
                    CreatedByUserId = TestAccounts.AdminId, UpdatedByUserId = TestAccounts.AdminId, CreatedAtUtc = now, UpdatedAtUtc = now });
            return Task.CompletedTask;
        });
        foreach (var username in new[] { "dev-ipqa-1", "dev-ipqa-2" })
        {
            var auth = await f.Login(username); var client = f.Client(auth.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/line-patrol/lines")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/line-patrol/tasks/{id}")).StatusCode);
        }
        Assert.Equal(3, await f.Read(db => db.RegisteredDevices.CountAsync()));
    }
    [Fact]
    public async Task Directory_and_task_reads_filter_actual_scope_before_paging_and_never_use_assignee()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login(); var client = f.Client(auth.AccessToken);
        var one = await f.Task(); var two = await f.Task(); await f.Task(f.Line2); var legacy = await f.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        var before = await f.Read(db => db.PatrolTasks.CountAsync());
        var raw = await client.GetStringAsync("/api/line-patrol/lines"); Assert.Contains(f.Line1.ToString(), raw); Assert.Contains(f.Line2.ToString(), raw);
        Assert.DoesNotContain("device", raw, StringComparison.OrdinalIgnoreCase);
        var p1 = (await client.GetFromJsonAsync<PagedLineTasks>($"/api/line-patrol/tasks?lineId={f.Line1}&pageSize=1"))!;
        var p2 = (await client.GetFromJsonAsync<PagedLineTasks>($"/api/line-patrol/tasks?lineId={f.Line1}&pageSize=1&page=2"))!;
        Assert.Equal(2, p1.Total); Assert.Equal(2, p2.Total); Assert.NotEqual(p1.Items[0].Id, p2.Items[0].Id);
        Assert.Equal(new[] { one, two }.Order().ToArray(), p1.Items.Concat(p2.Items).Select(x => x.Id).Order().ToArray());
        var summary = p1.Items[0]; Assert.False(summary.CanExecute); Assert.Equal("FoundationOnly", summary.ReadFoundationStatus);
        Assert.Equal(f.Line1, summary.ValidatedAccess.LineId); Assert.Equal(TestAccounts.Ipqa1Id, summary.ValidatedAccess.UserId);
        var detail = (await client.GetFromJsonAsync<LineTaskDetail>($"/api/line-patrol/tasks/{one}"))!;
        Assert.Equal("Frozen item", detail.Items.Single().InspectionItem); Assert.False(detail.CanExecute);
        Assert.DoesNotContain("assignedInspector", await client.GetStringAsync($"/api/line-patrol/tasks/{one}"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/line-patrol/tasks/{legacy}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/line-patrol/tasks/{one}?expectedTaskLineId={f.Line2}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/patrol-tasks/{one}/draft", new TaskDraftInput(null, []))).StatusCode);
        var owner = await f.Login("dev-ipqa-2");
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client(owner.AccessToken).PostAsync($"/api/patrol-tasks/{one}/submit", null)).StatusCode);
        Assert.Equal(before, await f.Read(db => db.PatrolTasks.CountAsync())); Assert.Equal(0, await f.Read(db => db.ProvisioningAuditEvents.CountAsync()));
        Assert.Equal(0, await f.Read(db => db.RegisteredDevices.CountAsync()));
    }

    [Theory]
    [InlineData("missing-header")][InlineData("missing-source-time")][InlineData("zero-items")][InlineData("empty-source")]
    [InlineData("legacy-answer")][InlineData("legacy-sample")][InlineData("completed")][InlineData("unknown-state")]
    public async Task Incompatible_shared_rows_are_truthful_in_list_but_not_reinterpreted_in_detail(string defect)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login(); var id = await f.Task();
        await f.Edit(async db =>
        {
            var task = await db.PatrolTasks.Include(x => x.Items).SingleAsync();
            switch (defect)
            {
                case "missing-header": task.PlanNameSnapshot = null; break;
                case "missing-source-time": task.SourceStandardUpdatedAtUtc = null; break;
                case "zero-items": task.Items.Clear(); break;
                case "empty-source": task.Items[0].SourcePatrolStandardItemId = Guid.Empty; break;
                case "legacy-answer": task.Items[0].IsNa = true; task.Items[0].MachineCode = "historical"; break;
                case "legacy-sample": db.PatrolTaskItemSamples.Add(new PatrolTaskItemSample {
                    Id = Guid.NewGuid(), PatrolTaskItemId = task.Items[0].Id, SequenceNo = 1, InspectionValue = 2 }); break;
                case "completed": task.Status = "Completed"; task.CurrentRevisionNo = 1; break;
                default: task.Status = "UnsupportedFuture"; break;
            }
        });
        var client = f.Client(auth.AccessToken);
        var page = (await client.GetFromJsonAsync<PagedLineTasks>($"/api/line-patrol/tasks?lineId={f.Line1}"))!;
        Assert.Equal(1, page.Total); Assert.NotEqual("FoundationOnly", page.Items.Single().ReadFoundationStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/line-patrol/tasks/{id}")).StatusCode);
    }

    [Fact]
    public async Task Later_plan_standard_and_directory_labels_do_not_rewrite_task_identity_or_definitions()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var id = await f.Task(); var auth = await f.Login();
        await f.Edit(async db =>
        {
            var task = await db.PatrolTasks.Include(x => x.PatrolPlan).Include(x => x.PatrolStandard).SingleAsync();
            task.PatrolPlan.LineId = f.Line2; task.PatrolPlan.WorkflowVersion = WorkflowVersion.LineSharedV1;
            task.PatrolPlan.PlanName = "Changed plan"; task.PatrolStandard.PatrolStandardName = "Changed standard";
            (await db.ProductionLines.SingleAsync(x => x.Id == f.Line1)).Name = "Renamed directory line";
        });
        var detail = (await f.Client(auth.AccessToken).GetFromJsonAsync<LineTaskDetail>($"/api/line-patrol/tasks/{id}"))!;
        Assert.Equal(f.Line1, detail.Summary.LineId); Assert.Equal("Frozen line", detail.Summary.LineNameSnapshot);
        Assert.Equal("Frozen standard", detail.Summary.StandardNameSnapshot); Assert.Equal("Frozen plan", detail.Summary.PlanNameSnapshot);
        Assert.Contains("Renamed directory line", await f.Client(auth.AccessToken).GetStringAsync("/api/line-patrol/lines"));
    }

    [Theory]
    [InlineData("admin", HttpStatusCode.Forbidden)][InlineData("pqe", HttpStatusCode.Forbidden)]
    [InlineData("dev-ipqa-1", HttpStatusCode.OK)]
    public async Task Business_permission_does_not_expand_admin_pqe_or_legacy_web_roles(string username, HttpStatusCode status)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login(username); var client = f.Client(auth.AccessToken);
        Assert.Equal(status, (await client.GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(status, (await client.PostAsync("/api/line-patrol/access/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(username == "admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/production-lines")).StatusCode);
    }

    [Theory]
    [InlineData("page=0")][InlineData("pageSize=101")][InlineData("page=2147483647&pageSize=100")]
    public async Task Invalid_pagination_is_rejected_without_read_side_effects(string query)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Client(auth.AccessToken).GetAsync($"/api/line-patrol/tasks?lineId={f.Line1}&{query}")).StatusCode);
    }

    [Theory]
    [InlineData("inactive")][InlineData("missing")][InlineData("duplicate")][InlineData("malformed")]
    public async Task Invalid_line_configuration_is_visible_without_breaking_login_or_admin_management(string problem)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        if (problem == "inactive") await f.Edit(async db => { (await db.ProductionLines.SingleAsync(x => x.Id == f.Line1)).IsActive = false; });
        else f.Options.LineIds[1] = problem switch { "duplicate" => f.Options.LineIds[0], "missing" => Guid.NewGuid().ToString(), _ => "invalid" };
        var client = f.Client(auth.AccessToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/line-patrol/access/verify", null)).StatusCode);
    }
}
