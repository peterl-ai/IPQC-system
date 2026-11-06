using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
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

public sealed class DeviceRegistryTests
{
    [Fact]
    public async Task Lifecycle_versions_and_old_reservations_are_auditable_without_trust_or_history_transfer()
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var a = await app.LineAsync("A"); var b = await app.LineAsync("B"); var device = await app.DeviceAsync("PAD");
        Assert.Equal("PendingCredential", device.Status); Assert.Equal("NotEnrolled", device.CredentialStatus);
        Assert.False(device.CanExecuteLineShared); Assert.Null(device.LineId);
        device = await app.DeviceAction(device, "bind", new DeviceBindingInput(a.Id, device.Revision));
        Assert.Equal(2, device.BindingVersion);
        var updated = await app.Admin.PutAsJsonAsync($"/api/admin/devices/{device.Id}", new DeviceUpdateInput("New label", "SM-T500", "descriptive", device.Revision));
        updated.EnsureSuccessStatusCode(); device = (await updated.Content.ReadFromJsonAsync<DeviceOutput>())!;
        Assert.Equal(3, device.Revision); Assert.Equal(2, device.BindingVersion);
        device = await app.DeviceAction(device, "rebind", new DeviceBindingInput(b.Id, device.Revision, "Move reservation"));
        device = await app.DeviceAction(device, "unbind", new DeviceReasonInput(device.Revision, "Release"));
        device = await app.DeviceAction(device, "bind", new DeviceBindingInput(a.Id, device.Revision));
        var replacement = await app.DeviceAsync("Replacement");
        var reserved = await app.DeviceAction(device, "replace", new DeviceReplaceInput(replacement.Id, device.Revision, replacement.Revision, "Hardware replacement"));
        Assert.Equal(replacement.Id, reserved.Id); Assert.Equal(a.Id, reserved.LineId); Assert.Equal(2, reserved.BindingVersion);
        var old = await app.GetDevice(device.Id);
        Assert.Equal("Replaced", old.Status); Assert.Null(old.LineId); Assert.Equal(replacement.Id, old.ReplacedByDeviceId);
        Assert.Equal(6, old.BindingVersion); Assert.False(old.CanExecuteLineShared); Assert.Equal(DateTimeKind.Utc, old.TerminalAtUtc!.Value.Kind);
        var audit = await app.DeviceAudit(old.Id);
        Assert.Equal(7, audit.Total);
        Assert.Contains(audit.Items, x => x.Before?.LineId == a.Id && x.After.LineId == b.Id);
        Assert.Contains(audit.Items, x => x.Before?.BindingVersion == 5 && x.After.BindingVersion == 6 && x.After.LineId is null);
        var oldEvent = audit.Items.Single(x => x.Action == "DeviceReplaced");
        var newEvent = (await app.DeviceAudit(replacement.Id)).Items.Single(x => x.Action == "ReplacementReserved");
        Assert.Equal(oldEvent.ChangeId, newEvent.ChangeId);
        Assert.All(audit.Items, x => { Assert.Equal(TestAccounts.AdminId, x.ActorUserId); Assert.Equal("admin", x.ActorUsername); Assert.Equal(DateTimeKind.Utc, x.OccurredAtUtc.Kind); });
        var revoked = await app.DeviceAction(reserved, "revoke", new DeviceReasonInput(reserved.Revision, "Retired"));
        Assert.Equal("Revoked", revoked.Status); Assert.Equal(3, revoked.BindingVersion); Assert.Null(revoked.LineId);
        foreach (var terminal in new[] { old, revoked })
        {
            var before = await app.StateAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync($"/api/admin/devices/{terminal.Id}/bind", new DeviceBindingInput(b.Id, terminal.Revision))).StatusCode);
            Assert.Equal(before, await app.StateAsync());
        }
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("rebind")]
    [InlineData("unbind")]
    [InlineData("revoke")]
    [InlineData("replace")]
    [InlineData("deactivate")]
    public async Task Audit_failure_rolls_back_all_state_including_replacement_first_save(string action)
    {
        var failure = new FailProvisioningAudit();
        await using var app = await ProvisioningFixture.CreateAsync(failure);
        var a = await app.LineAsync("A"); var b = await app.LineAsync("B"); var device = await app.DeviceAsync("PAD"); var target = await app.DeviceAsync("New");
        if (action is not ("bind" or "deactivate")) device = await app.DeviceAction(device, "bind", new DeviceBindingInput(a.Id, device.Revision));
        var before = await app.StateAsync(); failure.Armed = true;
        var response = action switch
        {
            "bind" => await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/bind", new DeviceBindingInput(a.Id, device.Revision)),
            "rebind" => await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/rebind", new DeviceBindingInput(b.Id, device.Revision, "Move")),
            "replace" => await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/replace", new DeviceReplaceInput(target.Id, device.Revision, target.Revision, "Replace")),
            "deactivate" => await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{a.Id}/deactivate", new ProvisioningRevisionInput(a.Revision)),
            _ => await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/{action}", new DeviceReasonInput(device.Revision, "Reason"))
        };
        Assert.True(failure.Fired); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, await app.StateAsync());
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("rebind")]
    [InlineData("replace")]
    public async Task Competing_devices_cannot_claim_the_same_line(string action)
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var line = await app.LineAsync("Target"); var left = await app.LineAsync("Left"); var right = await app.LineAsync("Right");
        var a = await app.DeviceAsync("A"); var b = await app.DeviceAsync("B"); var target = await app.DeviceAsync("New");
        if (action == "rebind")
        { a = await app.DeviceAction(a, "bind", new DeviceBindingInput(left.Id, a.Revision)); b = await app.DeviceAction(b, "bind", new DeviceBindingInput(right.Id, b.Revision)); }
        if (action == "replace") a = await app.DeviceAction(a, "bind", new DeviceBindingInput(line.Id, a.Revision));
        var beforeCount = await app.AuditCount();
        var responses = action == "replace" ? await Task.WhenAll(
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{a.Id}/replace", new DeviceReplaceInput(target.Id, a.Revision, target.Revision, "Replace")),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{b.Id}/bind", new DeviceBindingInput(line.Id, b.Revision))) : await Task.WhenAll(
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{a.Id}/{action}", new DeviceBindingInput(line.Id, a.Revision, "Move")),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{b.Id}/{action}", new DeviceBindingInput(line.Id, b.Revision, "Move")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        using var scope = app.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.Equal(1, await db.RegisteredDevices.CountAsync(x => x.LineId == line.Id));
        Assert.Equal(beforeCount + (action == "replace" ? 2 : 1), await app.AuditCount());
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("replace")]
    [InlineData("rebind")]
    [InlineData("unbind")]
    public async Task Same_revision_lifecycle_races_accept_only_one_complete_change(string action)
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var line = await app.LineAsync("A"); var other = await app.LineAsync("B"); var d = await app.DeviceAsync("PAD");
        d = await app.DeviceAction(d, "bind", new DeviceBindingInput(line.Id, d.Revision));
        var replacement = await app.DeviceAsync("New"); var before = await app.AuditCount();
        Task<HttpResponseMessage> Send() => action switch
        {
            "replace" => app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/replace", new DeviceReplaceInput(replacement.Id, d.Revision, replacement.Revision, "Replacement")),
            "rebind" => app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/rebind", new DeviceBindingInput(other.Id, d.Revision, "Move")),
            _ => app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/{action}", new DeviceReasonInput(d.Revision, "Retire"))
        };
        var results = await Task.WhenAll(Send(), Send());
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(before + (action == "replace" ? 2 : 1), await app.AuditCount());
    }

    [Fact]
    public async Task Two_replacements_cannot_take_the_same_new_identity_or_move_the_losing_devices_history()
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var left = await app.LineAsync("Left"); var right = await app.LineAsync("Right");
        var a = await app.DeviceAsync("A"); var b = await app.DeviceAsync("B"); var target = await app.DeviceAsync("New");
        a = await app.DeviceAction(a, "bind", new DeviceBindingInput(left.Id, a.Revision));
        b = await app.DeviceAction(b, "bind", new DeviceBindingInput(right.Id, b.Revision));
        var before = await app.AuditCount();
        var responses = await Task.WhenAll(
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{a.Id}/replace", new DeviceReplaceInput(target.Id, a.Revision, target.Revision, "Replace A")),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{b.Id}/replace", new DeviceReplaceInput(target.Id, b.Revision, target.Revision, "Replace B")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var originals = new[] { await app.GetDevice(a.Id), await app.GetDevice(b.Id) };
        var retired = Assert.Single(originals, x => x.Status == "Replaced"); var survivor = Assert.Single(originals, x => x.Status == "PendingCredential");
        Assert.Equal(retired.Id == a.Id ? right.Id : left.Id, survivor.LineId);
        Assert.Equal(2, survivor.BindingVersion); Assert.Equal(2, survivor.Revision);
        Assert.Equal(retired.Id == a.Id ? left.Id : right.Id, (await app.GetDevice(target.Id)).LineId);
        Assert.Equal(before + 2, await app.AuditCount());
    }

    [Fact]
    public async Task Bind_and_line_deactivation_race_has_no_inactive_reserved_line()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("A"); var d = await app.DeviceAsync("PAD");
        var before = await app.AuditCount();
        var results = await Task.WhenAll(
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/bind", new DeviceBindingInput(line.Id, d.Revision)),
            app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate", new ProvisioningRevisionInput(line.Revision)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        using var scope = app.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var current = await db.ProductionLines.SingleAsync();
        Assert.True(current.IsActive || !await db.RegisteredDevices.AnyAsync(x => x.LineId == current.Id));
        Assert.Equal(before + 1, await app.AuditCount());
    }

    [Fact]
    public async Task Invalid_transitions_stale_versions_and_missing_lines_leave_no_audit_or_binding_changes()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("A", false); var d = await app.DeviceAsync("PAD");
        var before = await app.StateAsync();
        foreach (var request in new[] {
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/bind", new DeviceBindingInput(line.Id, d.Revision)),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/bind", new DeviceBindingInput(Guid.NewGuid(), d.Revision)),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/unbind", new DeviceReasonInput(d.Revision, "Reason")),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke", new DeviceReasonInput(d.Revision, " ")),
            app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/replace", new DeviceReplaceInput(d.Id, d.Revision, d.Revision, "Reason")) })
        { using var response = await request; Assert.False(response.IsSuccessStatusCode); }
        Assert.Equal(before, await app.StateAsync());
    }
}

internal sealed class FailProvisioningAudit : SaveChangesInterceptor
{
    public bool Armed { get; set; }
    public bool Fired { get; private set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Armed && eventData.Context!.ChangeTracker.Entries<ProvisioningAuditEvent>().Any(x => x.State == EntityState.Added))
        { Armed = false; Fired = true; throw new InvalidOperationException("Injected audit persistence failure."); }
        return ValueTask.FromResult(result);
    }
}

internal sealed class ProvisioningFixture : IAsyncDisposable
{
    public required WebApplicationFactory<Program> Factory { get; init; }
    public required HttpClient Admin { get; init; }
    public required string Path { get; init; }
    public static async Task<ProvisioningFixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ipqc-provisioning-{Guid.NewGuid():N}.db");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing"); web.ConfigureLogging(x => x.ClearProviders());
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                services.AddDbContext<IpqcDbContext>(o => { o.UseSqlite($"Data Source={path};Default Timeout=5"); if (interceptor is not null) o.AddInterceptors(interceptor); });
            });
        });
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
        await TestAccounts.SeedAsync(factory);
        return new ProvisioningFixture { Factory = factory, Path = path, Admin = await TestAccounts.ClientAsync(factory, "admin") };
    }
    public async Task<LineOutput> LineAsync(string code, bool active = true)
    { var r = await Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput(code, code + " name", active)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<LineOutput>())!; }
    public async Task<DeviceOutput> DeviceAsync(string name)
    { var r = await Admin.PostAsJsonAsync("/api/admin/devices", new DeviceCreateInput(name)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<DeviceOutput>())!; }
    public async Task<DeviceOutput> DeviceAction<T>(DeviceOutput d, string action, T input)
    { var r = await Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/{action}", input); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<DeviceOutput>())!; }
    public async Task<DeviceOutput> GetDevice(Guid id) => (await Admin.GetFromJsonAsync<DeviceOutput>($"/api/admin/devices/{id}"))!;
    public async Task<ProvisioningPage<ProvisioningAuditOutput>> DeviceAudit(Guid id) => (await Admin.GetFromJsonAsync<ProvisioningPage<ProvisioningAuditOutput>>($"/api/admin/devices/{id}/audit"))!;
    public async Task<int> AuditCount() { using var scope = Factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().ProvisioningAuditEvents.CountAsync(); }
    public async Task<string> StateAsync()
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        return JsonSerializer.Serialize(new object[] { await db.ProductionLines.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            await db.RegisteredDevices.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.ProvisioningAuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync() });
    }
    public async ValueTask DisposeAsync() { Admin.Dispose(); await Factory.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(Path); }
}
