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
    public async Task Controlled_revoke_releases_existing_reservation_without_touching_other_history()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("A");
        var device = await app.DeviceAsync("Historical PAD", line.Id); var before = await app.DeviceAudit(device.Id);
        var revoked = await app.DeviceAction(device, "revoke", new DeviceReasonInput(device.Revision, "Retire"));
        Assert.Equal("Revoked", revoked.Status); Assert.Null(revoked.LineId);
        Assert.Equal(device.BindingVersion + 1, revoked.BindingVersion); Assert.False(revoked.CanExecuteLineShared);
        var history = await app.DeviceAudit(device.Id);
        foreach (var old in before.Items) Assert.Contains(old, history.Items);
        var audit = Assert.Single(history.Items, x => x.Action == "DeviceRevoked");
        Assert.Equal(line.Id, audit.Before!.LineId); Assert.Equal("Retire", audit.Reason);
        Assert.Equal(TestAccounts.AdminId, audit.ActorUserId);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/revoke", new DeviceReasonInput(revoked.Revision, "Again"))).StatusCode);
    }
    [Fact]
    public async Task Revoke_audit_failure_rolls_back_reservation_versions_and_status()
    {
        var failure = new FailProvisioningAudit(); await using var app = await ProvisioningFixture.CreateAsync(failure);
        var line = await app.LineAsync("A"); var d = await app.DeviceAsync("Historical", line.Id);
        var before = await app.StateAsync(); failure.Armed = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke", new DeviceReasonInput(d.Revision, "Retire"))).StatusCode);
        Assert.True(failure.Fired); Assert.Equal(before, await app.StateAsync());
    }
    [Fact]
    public async Task Concurrent_same_revision_revokes_have_one_winner()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var line = await app.LineAsync("A"); var d = await app.DeviceAsync("Historical", line.Id);
        var before = await app.AuditCount();
        var replies = await Task.WhenAll(Enumerable.Range(0,2).Select(_=>app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke",new DeviceReasonInput(d.Revision,"Retire"))));
        Assert.Single(replies,x=>x.IsSuccessStatusCode); Assert.Single(replies,x=>x.StatusCode==HttpStatusCode.Conflict);
        Assert.Equal(before+1,await app.AuditCount());
    }
    [Fact]
    public async Task Line_deactivation_audit_failure_preserves_active_state_and_revision()
    {
        var failure=new FailProvisioningAudit();await using var app=await ProvisioningFixture.CreateAsync(failure);
        var line=await app.LineAsync("A");var before=await app.StateAsync();failure.Armed=true;
        Assert.Equal(HttpStatusCode.InternalServerError,(await app.Admin.PostAsJsonAsync($"/api/admin/production-lines/{line.Id}/deactivate",new ProvisioningRevisionInput(line.Revision))).StatusCode);
        Assert.True(failure.Fired);Assert.Equal(before,await app.StateAsync());
    }
    [Fact]
    public async Task Invalid_missing_and_stale_revoke_preserve_existing_contracts()
    {
        await using var app = await ProvisioningFixture.CreateAsync(); var d = await app.DeviceAsync("Historical"); var before = await app.StateAsync();
        Assert.Equal(HttpStatusCode.BadRequest,(await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke",new DeviceReasonInput(d.Revision," "))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await app.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke",new DeviceReasonInput(d.Revision+1,"Retire"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await app.Admin.PostAsJsonAsync($"/api/admin/devices/{Guid.NewGuid()}/revoke",new DeviceReasonInput(1,"Retire"))).StatusCode);
        Assert.Equal(before,await app.StateAsync());
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
    public static async Task<ProvisioningFixture> CreateAsync(SaveChangesInterceptor? interceptor = null, Action<IServiceCollection>? configure = null, string environment = "Testing", Dictionary<string,string?>? settings = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ipqc-provisioning-{Guid.NewGuid():N}.db");
        if (environment == "Production")
        {
            await using var history = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options);
            await history.Database.MigrateAsync();
            await TestAccounts.SeedAsync(history, new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>());
        }
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment(environment); web.ConfigureLogging(x => x.ClearProviders());
            // UseSetting reaches minimal-host startup reads before builder.Build; late app configuration does not.
            web.UseSetting("Authentication:JwtSigningKey", "Synthetic-R32-01-authentication-key-for-test-host-only");
            web.UseSetting("Database:Provider", environment == "Production" ? "Postgres" : "Sqlite");
            foreach (var setting in settings ?? []) web.UseSetting(setting.Key, setting.Value);
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>(settings ?? []) { ["Scheduler:Enabled"] = "false", ["Database:Provider"] = environment == "Production" ? "Postgres" : "Sqlite", ["Authentication:JwtSigningKey"] = "Synthetic-R32-01-authentication-key-for-test-host-only" }));
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<IpqcDbContext>>();
                services.AddDbContext<IpqcDbContext>(o => { o.UseSqlite($"Data Source={path};Default Timeout=5"); if (interceptor is not null) o.AddInterceptors(interceptor); });
                configure?.Invoke(services);
            });
        });
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
        await TestAccounts.SeedAsync(factory);
        return new ProvisioningFixture { Factory = factory, Path = path, Admin = await TestAccounts.ClientAsync(factory, "admin") };
    }
    public async Task<LineOutput> LineAsync(string code, bool active = true)
    { var r = await Admin.PostAsJsonAsync("/api/admin/production-lines", new LineCreateInput(code, code + " name", active)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<LineOutput>())!; }
    // Synthetic pre-retirement history, never an application enrollment/provisioning bypass.
    public async Task<DeviceOutput> DeviceAsync(string name, Guid? lineId = null)
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var device = new RegisteredDevice { Id = Guid.NewGuid(), DisplayName = name, LineId = lineId,
            Revision = lineId is null ? 1 : 2, BindingVersion = lineId is null ? 1 : 2,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = TestAccounts.AdminId, UpdatedByUserId = TestAccounts.AdminId };
        db.RegisteredDevices.Add(device);
        scope.ServiceProvider.GetRequiredService<JaxPower.Ipqc.Api.Services.ProvisioningStore>().Audit(null, device.Id,
            "DeviceCreated", TestAccounts.AdminId, "admin", null,
            JaxPower.Ipqc.Api.Services.DeviceRegistryService.Snapshot(device));
        await db.SaveChangesAsync(); return JaxPower.Ipqc.Api.Services.DeviceRegistryService.Output(device);
    }
    public async Task<T> Db<T>(Func<IpqcDbContext, Task<T>> operation)
    { using var scope = Factory.Services.CreateScope(); return await operation(scope.ServiceProvider.GetRequiredService<IpqcDbContext>()); }
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
