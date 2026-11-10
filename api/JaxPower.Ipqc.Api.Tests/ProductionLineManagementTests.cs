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
    public async Task Existing_reservations_restrict_deactivation_until_controlled_revoke()
    {
        await using var app=await ProvisioningFixture.CreateAsync();var line=await app.LineAsync("A");var d=await app.DeviceAsync("Historical",line.Id);
        var before=await app.StateAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate",new ProvisioningRevisionInput(line.Revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,(await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/unbind",new DeviceReasonInput(d.Revision,"Release"))).StatusCode);
        Assert.Equal(before,await app.StateAsync());
        await app.Db(async db=>{await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id={line.Id}"));return 0;});
        await app.DeviceAction(d,"revoke",new DeviceReasonInput(d.Revision,"Retire"));
        (await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate",new ProvisioningRevisionInput(line.Revision))).EnsureSuccessStatusCode();
        await app.Db(async db=>{await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id={line.Id}"));return 0;});
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
    public async Task Retired_device_calls_and_retained_revoke_preserve_legacy_reports_and_LineShared_guards()
    {
        await using var app=await WorkflowFixture.CreateAsync();var completed=await app.SeedAsync(WorkflowVersion.LegacyPersonalV1,"Completed");var shared=await app.SeedAsync(WorkflowVersion.LineSharedV1);
        var id=Guid.NewGuid();using(var scope=app.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IpqcDbContext>();db.RegisteredDevices.Add(new(){Id=id,DisplayName="Historical",LineId=shared.Line,Revision=2,BindingVersion=2,
                CreatedAtUtc=DateTime.UtcNow,UpdatedAtUtc=DateTime.UtcNow,CreatedByUserId=TestAccounts.AdminId,UpdatedByUserId=TestAccounts.AdminId});await db.SaveChangesAsync();
        }
        var before=await app.StateAsync();var report=$"/api/completed-patrol-records/{completed.Task}/report";var oldReport=await app.Admin.GetStringAsync(report);
        app.Ipqa.DefaultRequestHeaders.Add("X-Device-Id",id.ToString());app.Ipqa.DefaultRequestHeaders.Add("X-Client-Cert","forged");
        foreach(var (method,path) in DeviceProvisioningRetirementTests.Routes(id))Assert.Equal(HttpStatusCode.Gone,(await app.Admin.SendAsync(new(method,path){Content=JsonContent.Create(new{status="Trusted"})})).StatusCode);
        (await app.Admin.PostAsJsonAsync($"/api/admin/devices/{id}/revoke",new DeviceReasonInput(2,"Retire"))).EnsureSuccessStatusCode();
        foreach(var action in new[]{"draft","submit"})Assert.Equal(HttpStatusCode.Conflict,(await app.MutateTaskAsync(action,shared.Task,shared.Item,shared.Task)).StatusCode);
        Assert.Equal(before,await app.StateAsync());Assert.Equal(oldReport,await app.Admin.GetStringAsync(report));
        var routes=app.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(x=>x.RoutePattern.RawText??"");
        Assert.DoesNotContain(routes,x=>x.Contains("native",StringComparison.OrdinalIgnoreCase)||x.Contains("offline-grant",StringComparison.OrdinalIgnoreCase));
    }
}