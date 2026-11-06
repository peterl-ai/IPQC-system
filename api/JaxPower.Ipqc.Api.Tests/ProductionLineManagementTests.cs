using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ProductionLineManagementTests
{
    [Fact]
    public async Task Directory_normalizes_codes_preserves_ids_and_has_stale_update_and_append_only_audit_protection()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("  line-a  ");
        Assert.Equal("line-a", line.Code);
        var before = await app.StateAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput("LINE-A", "Duplicate"))).StatusCode);
        Assert.Equal(before, await app.StateAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput(" ", "Name"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput("B", new string('x', 201)))).StatusCode);
        var renamed = await app.Admin.PutAsJsonAsync($"/api/admin/production-lines/{line.Id}", new LineRenameInput("New name", line.Revision));
        renamed.EnsureSuccessStatusCode(); var row = (await renamed.Content.ReadFromJsonAsync<LineOutput>())!;
        Assert.Equal(line.Id, row.Id); Assert.Equal(line.Code, row.Code); Assert.Equal(2, row.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate", new ProvisioningRevisionInput(line.Revision))).StatusCode);
        var result = await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate", new ProvisioningRevisionInput(row.Revision)); result.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput("LINE-A", "Inactive duplicate"))).StatusCode);
        result = await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/activate", new ProvisioningRevisionInput(3)); result.EnsureSuccessStatusCode();
        var list = (await app.Admin.GetFromJsonAsync<ProvisioningPage<LineOutput>>("/api/admin/production-lines?active=true&pageSize=1"))!;
        Assert.Equal(1, list.Total); Assert.Equal(4, Assert.Single(list.Items).Revision);
        var audit = (await app.Admin.GetFromJsonAsync<ProvisioningPage<ProvisioningAuditOutput>>($"/api/admin/production-lines/{line.Id}/audit"))!;
        Assert.Equal(4, audit.Total); Assert.Equal(line.Name, audit.Items.Single(x => x.Action == "LineCreated").After.Name);
        using var scope = app.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var entry = await db.ProvisioningAuditEvents.FirstAsync(); entry.Action = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        entry = await db.ProvisioningAuditEvents.FirstAsync(); db.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id = {line.Id}"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Current_and_historical_reservations_have_restrictive_line_foreign_keys()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var a = await app.LineAsync("A"); var b = await app.LineAsync("B"); var d = await app.DeviceAsync("PAD");
        d = await app.DeviceAction(d, "bind", new DeviceBindingInput(a.Id, d.Revision));
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{a.Id}/deactivate", new ProvisioningRevisionInput(a.Revision))).StatusCode);
        d = await app.DeviceAction(d, "rebind", new DeviceBindingInput(b.Id, d.Revision, "Move"));
        using var scope = app.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        foreach (var line in new[] { a, b }) await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id = {line.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RegisteredDevices WHERE Id = {d.Id}"));
        var other = await app.DeviceAsync("Other");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RegisteredDevices SET LineId = {b.Id} WHERE Id = {other.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RegisteredDevices SET Status = {4} WHERE Id = {other.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RegisteredDevices SET BindingVersion = {0} WHERE Id = {other.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RegisteredDevices SET LineId = {Guid.NewGuid()} WHERE Id = {other.Id}"));
    }

    [Theory]
    [InlineData("pqe")]
    [InlineData("dev-ipqa-1")]
    [InlineData("anonymous")]
    [InlineData("forced")]
    public async Task Every_provisioning_route_requires_admin_permissions(string user)
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("A"); var d = await app.DeviceAsync("PAD");
        if (user == "forced")
        {
            using var scope = app.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var admin = await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.AdminId); admin.MustChangePassword = true; await db.SaveChangesAsync();
        }
        using var client = user == "anonymous" ? app.Factory.CreateClient() : await TestAccounts.ClientAsync(app.Factory, user == "forced" ? "admin" : user);
        var expected = user == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var before = await app.StateAsync();
        var routes = new (HttpMethod Method, string Path, object? Input)[] {
            (HttpMethod.Get, "/api/admin/production-lines", null), (HttpMethod.Get, $"/api/admin/production-lines/{line.Id}", null),
            (HttpMethod.Get, $"/api/admin/production-lines/{line.Id}/audit", null), (HttpMethod.Post, "/api/admin/production-lines", new LineCreateInput("B", "B")),
            (HttpMethod.Put, $"/api/admin/production-lines/{line.Id}", new LineRenameInput("Rename", 1)),
            (HttpMethod.Post, $"/api/admin/production-lines/{line.Id}/activate", new ProvisioningRevisionInput(1)),
            (HttpMethod.Post, $"/api/admin/production-lines/{line.Id}/deactivate", new ProvisioningRevisionInput(1)),
            (HttpMethod.Get, "/api/admin/devices", null), (HttpMethod.Get, $"/api/admin/devices/{d.Id}", null), (HttpMethod.Get, $"/api/admin/devices/{d.Id}/audit", null),
            (HttpMethod.Post, "/api/admin/devices", new DeviceCreateInput("Other")), (HttpMethod.Put, $"/api/admin/devices/{d.Id}", new DeviceUpdateInput("Edit", null, null, 1)),
            (HttpMethod.Post, $"/api/admin/devices/{d.Id}/bind", new DeviceBindingInput(line.Id, 1)),
            (HttpMethod.Post, $"/api/admin/devices/{d.Id}/rebind", new DeviceBindingInput(line.Id, 1, "Move")),
            (HttpMethod.Post, $"/api/admin/devices/{d.Id}/unbind", new DeviceReasonInput(1, "Reason")),
            (HttpMethod.Post, $"/api/admin/devices/{d.Id}/revoke", new DeviceReasonInput(1, "Reason")),
            (HttpMethod.Post, $"/api/admin/devices/{d.Id}/replace", new DeviceReplaceInput(Guid.NewGuid(), 1, 1, "Reason")) };
        foreach (var (method, path, input) in routes)
        {
            using var request = new HttpRequestMessage(method, path) { Content = input is null ? null : JsonContent.Create(input) };
            Assert.Equal(expected, (await client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(before, await app.StateAsync());
    }

    [Fact]
    public async Task Provisioning_cannot_issue_device_identity_or_unlock_LineShared_and_spoofed_inputs_fail_closed()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var task = await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var create = await app.Admin.PostAsJsonAsync("/api/admin/devices", new DeviceCreateInput("Descriptive only")); create.EnsureSuccessStatusCode();
        var d = (await create.Content.ReadFromJsonAsync<DeviceOutput>())!;
        var bound = await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/bind", new DeviceBindingInput(task.Line, 1)); bound.EnsureSuccessStatusCode();
        var pending = (await bound.Content.ReadFromJsonAsync<DeviceOutput>())!;
        Assert.False(pending.CanExecuteLineShared); Assert.Equal("NotEnrolled", pending.CredentialStatus);
        app.Ipqa.DefaultRequestHeaders.Add("X-Device-Id", d.Id.ToString());
        app.Ipqa.DefaultRequestHeaders.Add("X-Client-Cert", "forged"); app.Ipqa.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        var before = await app.StateAsync();
        foreach (var revoked in new[] { false, true })
        {
            var version = pending;
            if (revoked)
            {
                var response = await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke", new DeviceReasonInput(pending.Revision, "Revoke"));
                response.EnsureSuccessStatusCode(); version = (await response.Content.ReadFromJsonAsync<DeviceOutput>())!;
            }
            app.Ipqa.DefaultRequestHeaders.Remove("X-Binding-Version"); app.Ipqa.DefaultRequestHeaders.Add("X-Binding-Version", version.BindingVersion.ToString());
            foreach (var claimedLine in new[] { task.Line, Guid.NewGuid() })
            {
                app.Ipqa.DefaultRequestHeaders.Remove("X-Line-Id"); app.Ipqa.DefaultRequestHeaders.Add("X-Line-Id", claimedLine.ToString());
                Assert.Equal(HttpStatusCode.Conflict, (await app.MutateTaskAsync("draft", task.Task, task.Item, task.Task)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await app.MutateTaskAsync("submit", task.Task, task.Item, task.Task)).StatusCode);
            }
        }
        Assert.Equal(before, await app.StateAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Admin.PostAsJsonAsync("/api/admin/devices", new { displayName = "Forged", status = "Trusted", bindingVersion = 99, lineId = task.Line, fingerprint = "fake" })).StatusCode);
        var routes = app.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(x => x.RoutePattern.RawText ?? "").ToList();
        Assert.DoesNotContain(routes, x => x.Contains("pair", StringComparison.OrdinalIgnoreCase) || x.Contains("enroll", StringComparison.OrdinalIgnoreCase) || x.Contains("certificate", StringComparison.OrdinalIgnoreCase) || x.Contains("offline-grant", StringComparison.OrdinalIgnoreCase));
        foreach (var path in new[] { "/api/pairing", "/api/devices/enroll", "/api/native/session" }) Assert.Equal(HttpStatusCode.NotFound, (await app.Admin.PostAsJsonAsync(path, new { deviceId = d.Id })).StatusCode);
        Assert.DoesNotContain(routes, x => x.Contains("{id:guid}/audit") && x.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reservation_rebind_replacement_and_revoke_preserve_all_legacy_work_and_completed_report_facts()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var completed = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed");
        var shared = await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var before = await app.StateAsync();
        var reportPath = $"/api/completed-patrol-records/{completed.Task}/report";
        var reportBefore = await app.Admin.GetStringAsync(reportPath);
        async Task<DeviceOutput> Create(string name)
        {
            var response = await app.Admin.PostAsJsonAsync("/api/admin/devices", new DeviceCreateInput(name));
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<DeviceOutput>())!;
        }
        async Task<DeviceOutput> Change<T>(DeviceOutput device, string action, T input)
        {
            var response = await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/{action}", input);
            response.EnsureSuccessStatusCode();
            Assert.Equal(before, await app.StateAsync());
            Assert.Equal(reportBefore, await app.Admin.GetStringAsync(reportPath));
            return (await response.Content.ReadFromJsonAsync<DeviceOutput>())!;
        }
        var old = await Create("Old PAD"); var replacement = await Create("New PAD");
        old = await Change(old, "bind", new DeviceBindingInput(completed.Line, old.Revision));
        old = await Change(old, "rebind", new DeviceBindingInput(shared.Line, old.Revision, "Move reservation"));
        replacement = await Change(old, "replace", new DeviceReplaceInput(replacement.Id, old.Revision, replacement.Revision, "Replace hardware"));
        await Change(replacement, "revoke", new DeviceReasonInput(replacement.Revision, "Retired"));
    }
}
