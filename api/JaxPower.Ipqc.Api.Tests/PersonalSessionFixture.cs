using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed class PersonalClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

// Pauses or fails real relational commands after the old refresh row was consumed.
internal sealed class SessionFault : DbCommandInterceptor
{
    private Func<DbCommand, bool>? match;
    private bool fail;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Arm(string sqlFragment, bool failure = false)
    {
        fail = failure;
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        match = c => c.CommandText.Contains(sqlFragment, StringComparison.Ordinal);
    }
    private async Task Check(DbCommand command, CancellationToken ct)
    {
        var predicate = match;
        if (predicate is null || !predicate(command) || Interlocked.CompareExchange(ref match, null, predicate) != predicate) return;
        Entered.TrySetResult();
        if (fail) throw new InvalidOperationException("Synthetic session persistence failure");
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
    }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { await Check(command, cancellationToken); return result; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await Check(command, cancellationToken); return result; }
}

internal sealed class PersonalSessionFixture : IAsyncDisposable
{
    public required WebApplicationFactory<Program> Factory { get; init; }
    public required HttpClient Anonymous { get; init; }
    public required PersonalClock Clock { get; init; }
    public required SessionFault Fault { get; init; }
    public required PersonalAccessOptions Options { get; init; }
    public required string Connection { get; init; }
    public string? PostgresRoot { get; init; }
    public Guid Line1 => Guid.Parse(Options.LineIds[0]);
    public Guid Line2 => Guid.Parse(Options.LineIds[1]);
    public const string Environment = "synthetic-r3202-test";

    public static async Task<PersonalSessionFixture> CreateAsync(bool postgres = false, string? environment = Environment,
        string? migration = null)
    {
        var clock = new PersonalClock(); var fault = new SessionFault();
        var options = new PersonalAccessOptions { EnvironmentId = environment,
            LineIds = [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()] };
        string connection; string? root = null;
        if (postgres)
        {
            root = System.Environment.GetEnvironmentVariable("IPQC_TEST_POSTGRES_CONNECTION");
            if (string.IsNullOrWhiteSpace(root) || System.Environment.GetEnvironmentVariable("IPQC_TEST_POSTGRES_DISPOSABLE") != "true")
                throw new InvalidOperationException("Explicit disposable PostgreSQL opt-in required.");
            var source = new NpgsqlConnectionStringBuilder(root);
            if (!source.Database!.StartsWith("ipqc_test", StringComparison.Ordinal))
                throw new InvalidOperationException("Only explicitly named ipqc_test databases are permitted.");
            var name = "ipqc_test_r3202_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(root); await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin); await create.ExecuteNonQueryAsync();
            source.Database = name; connection = source.ConnectionString;
        }
        else connection = $"Data Source={System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ipqc-r3202-{Guid.NewGuid():N}.db")};Default Timeout=2";
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing"); web.UseSetting("Scheduler:Enabled", "false");
            web.ConfigureLogging(x => x.ClearProviders());
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                services.RemoveAll<AuthenticationClock>(); services.AddSingleton(new AuthenticationClock(clock));
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<PersonalAccessOptions>(); services.AddSingleton(options);
                services.AddDbContext<IpqcDbContext>(o =>
                {
                    if (postgres) o.UseNpgsql(connection); else o.UseSqlite(connection);
                    o.AddInterceptors(fault);
                });
            });
        });
        var fixture = new PersonalSessionFixture { Factory = factory, Anonymous = factory.CreateClient(),
            Clock = clock, Fault = fault, Options = options, Connection = connection, PostgresRoot = root };
        await fixture.Edit(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync(migration);
            var hasher = fixture.Factory.Services.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<ApplicationUser>>();
            await TestAccounts.SeedAsync(db, hasher);
            db.ProductionLines.AddRange(new ProductionLine { Id = fixture.Line1, Code = "SYN-1", Name = "Synthetic Line One" },
                new ProductionLine { Id = fixture.Line2, Code = "SYN-2", Name = "Synthetic Line Two" });
        });
        return fixture;
    }
    public async Task Edit(Func<IpqcDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        await action(db); await db.SaveChangesAsync();
    }
    public async Task<T> Read<T>(Func<IpqcDbContext, Task<T>> action)
    { using var scope = Factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<IpqcDbContext>()); }
    public async Task<AuthOutput> Login(string username = "dev-ipqa-1", string password = TestAccounts.Password)
    {
        var response = await Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput(username, password, "Native"));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthOutput>())!;
    }
    public HttpClient Client(string token)
    { var client = Factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
    public async Task<AuthOutput> Refresh(string token)
    {
        var response = await Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(token));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthOutput>())!;
    }
    public string Token(AuthOutput auth, Func<List<Claim>, List<Claim>> change)
    {
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).Claims
            .Where(x => x.Type is not ("exp" or "nbf" or "iat" or "iss" or "aud")).ToList();
        var settings = Factory.Services.GetRequiredService<JwtSettings>();
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(settings.Issuer, settings.Audience,
            change(claims), Clock.Now.UtcDateTime, Clock.Now.UtcDateTime.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(settings.Key), SecurityAlgorithms.HmacSha256)));
    }
    public async Task<Guid> Task(Guid? line = null, WorkflowVersion workflow = WorkflowVersion.LineSharedV1)
    {
        var id = Guid.NewGuid();
        await Edit(db =>
        {
            var now = Clock.Now.UtcDateTime;
            var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Synthetic standard", LineName = "Synthetic", CreatedAtUtc = now, UpdatedAtUtc = now };
            var plan = new PatrolPlan { Id = Guid.NewGuid(), PlanNo = "P-" + id, PlanName = "Synthetic plan", PatrolStandard = standard,
                EffectiveStartUtc = now, GenerationNotBeforeUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.PatrolTasks.Add(new PatrolTask { Id = id, TaskNo = "T-" + id, PatrolPlan = plan, PatrolStandard = standard,
                WorkflowVersion = workflow, LineId = line ?? Line1, AssignedInspectorUserId = TestAccounts.Ipqa2Id,
                AssignedInspectorKey = "dev-ipqa-2", ScheduledOccurrenceUtc = now, GeneratedAtUtc = now,
                CreatedAtUtc = now, UpdatedAtUtc = now, PlanNoSnapshot = plan.PlanNo, PlanNameSnapshot = "Frozen plan",
                StandardNameSnapshot = "Frozen standard", LineNameSnapshot = "Frozen line", SourceStandardUpdatedAtUtc = now,
                Items = [new PatrolTaskItem { Id = Guid.NewGuid(), SourcePatrolStandardItemId = Guid.NewGuid(),
                    SequenceNo = 1, InspectionItem = "Frozen item", InspectionType = "Qualitative", SampleCount = "1", PhotoRequirement = "historical-raw" }] });
            return System.Threading.Tasks.Task.CompletedTask;
        });
        return id;
    }
    public async ValueTask DisposeAsync()
    {
        Fault.Release.TrySetResult(); Anonymous.Dispose(); await Factory.DisposeAsync();
        if (PostgresRoot is null) { SqliteConnection.ClearAllPools(); File.Delete(new SqliteConnectionStringBuilder(Connection).DataSource); }
        else
        {
            NpgsqlConnection.ClearAllPools(); var name = new NpgsqlConnectionStringBuilder(Connection).Database;
            if (name is null || !name.StartsWith("ipqc_test_r3202_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup target.");
            await using var admin = new NpgsqlConnection(PostgresRoot); await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
