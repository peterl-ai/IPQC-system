using System.Data;
using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static JaxPower.Ipqc.Api.Tests.WorkContextFixture;

namespace JaxPower.Ipqc.Api.Tests;

internal static class WorkContextRevocationRace
{
    // Real server lock state, not a delay, establishes snapshot-before-lock-wait.
    internal static async Task WaitingSecurity(PersonalSessionFixture f, string action, ITestOutputHelper output, bool observeSnapshot = true)
    {
        var task = await Task(f); var a = await f.Login(); var b = await f.Login("dev-ipqa-2");
        using var scope = f.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ApplicationUserService>();
        var issuerPid = 0;
        var securitySeen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotSeen = new TaskCompletionSource<(string Isolation, string Snapshot, long Visible)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var locks = new System.Collections.Concurrent.ConcurrentQueue<(int Pid, IsolationLevel? Isolation)>();
        var securityTransactions = new System.Collections.Concurrent.ConcurrentDictionary<NpgsqlTransaction, byte>();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var snapshots = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var firstSnapshot = 0;
        f.Fault.ObserveAsync = async (command, ct) => {
            if (command.Connection is not NpgsqlConnection connection) return;
            if (command.CommandText.Contains("INSERT INTO \"WorkContexts\"", StringComparison.Ordinal))
            {
                issuerPid = connection.ProcessID;
                output.WriteLine($"issuer pid={issuerPid}, isolation={command.Transaction!.IsolationLevel}");
            }
            if (issuerPid == 0) return;
            if (command.CommandText.Contains("FROM \"ApplicationUsers\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                locks.Enqueue((connection.ProcessID, command.Transaction?.IsolationLevel));
                securityTransactions.TryAdd((NpgsqlTransaction)command.Transaction!, 0);
                securitySeen.TrySetResult(connection.ProcessID);
            }
            if (command.Transaction is not NpgsqlTransaction transaction || !securityTransactions.ContainsKey(transaction) ||
                !command.CommandText.Contains("FROM \"WorkContexts\"", StringComparison.Ordinal)) return;
            if (!observeSnapshot) return; // Twins prove the production query itself, without an extra transactional read.
            // This query runs only after the real account-lock statement established its snapshot.
            // It observes the same transaction as the actual revocation query; no production hook.
            await using var probe = new NpgsqlCommand("SELECT current_setting('transaction_isolation'), pg_current_snapshot()::text, " +
                "(SELECT count(*) FROM \"WorkContexts\" WHERE \"UserId\" = @user)", connection, (NpgsqlTransaction)command.Transaction!);
            probe.Parameters.AddWithValue("user", TestAccounts.Ipqa1Id);
            await using var reader = await probe.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct));
            var observed = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
            snapshots.Enqueue($"security isolation={observed.Item1}, snapshot={observed.Item2}, visibleContexts={observed.Item3}");
            await reader.DisposeAsync();
            if (Interlocked.Increment(ref firstSnapshot) == 1)
            {
                snapshotSeen.TrySetResult(observed);
                await releaseSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
        };
        f.Fault.ObserveFailure = (_, error) => { if (error is PostgresException pg) failures.Enqueue(pg.SqlState); };
        f.Fault.Arm("INSERT INTO \"WorkContexts\"");
        Task<HttpResponseMessage>? issue = null; Task<UserOutput?>? security = null;
        try
        {
            issue = f.Client(a.AccessToken).PostAsJsonAsync(Root(task), Input(f));
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertKeyShareCompatible(f);
            security = action switch {
                "deactivate" => users.SetActiveAsync(TestAccounts.Ipqa1Id, false, TestAccounts.AdminId, "admin", default),
                "role" => users.UpdateAsync(TestAccounts.Ipqa1Id, new("Synthetic inspector", "PQE"), TestAccounts.AdminId, "admin", default),
                _ => users.ResetPasswordAsync(TestAccounts.Ipqa1Id, "SyntheticChanged123A", TestAccounts.AdminId, "admin", default)
            };
            var waiterPid = await securitySeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitForBlocker(f, waiterPid, issuerPid);
            Assert.False(security.IsCompleted);
            output.WriteLine($"server confirms security pid={waiterPid} blocked by issuer pid={issuerPid}");
            f.Fault.Release.TrySetResult();
            var response = await issue;
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var issued = (await response.Content.ReadFromJsonAsync<WorkContextIssued>())!;
            if (observeSnapshot)
            {
                var first = await snapshotSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(action == "reset" ? "read committed" : "serializable", first.Isolation);
                Assert.Equal(1, await f.Read(db => db.WorkContexts.CountAsync()));
                Assert.False(security.IsCompleted);
                output.WriteLine($"after issuer commit: independent reader sees1; security snapshot sees{first.Visible}");
            }
            releaseSnapshot.TrySetResult();
            var result = await security;
            f.Fault.ObserveAsync = null; f.Fault.ObserveFailure = null;
            Assert.NotNull(result);
            var account = await f.Read(db => db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == TestAccounts.Ipqa1Id));
            var contexts = await f.Read(db => db.WorkContexts.AsNoTracking().ToListAsync());
            var audit = await f.Read(db => db.WorkContextAuditEvents.AsNoTracking().ToListAsync());
            var live = await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.UserId == TestAccounts.Ipqa1Id && x.RevokedAtUtc == null));
            output.WriteLine($"committed action={action}, active={account.IsActive}, role={account.Role}, authVersion={account.AuthVersion}, " +
                $"liveSessions={live}, contexts={contexts.Count}, revoked={contexts.Count(x => x.SecurityRevokedAtUtc != null)}, audit={audit.Count}, lockAttempts={locks.Count}");
            foreach (var snapshot in snapshots) output.WriteLine(snapshot);
            output.WriteLine("observed SQLSTATE=" + string.Join(",", failures));
            Assert.Equal(action != "deactivate", result.IsActive);
            Assert.Equal(action == "role" ? "PQE" : "IPQA", result.Role);
            Assert.Equal(result.IsActive, account.IsActive); Assert.Equal(result.Role, account.Role);
            Assert.Equal(2, account.AuthVersion); Assert.Equal(action == "reset", account.MustChangePassword);
            Assert.Equal(0, live);
            Assert.All(locks, x => Assert.Equal(action == "reset" ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, x.Isolation));
            var context = Assert.Single(contexts);
            Assert.NotNull(context.SecurityRevokedAtUtc);
            Assert.Equal(2, context.Revision); Assert.Equal(TestAccounts.AdminId, context.SecurityRevokedByUserId);
            Assert.Equal(action switch { "deactivate" => "AccountDeactivated", "role" => "IpqaEligibilityLost", _ => "AdminPasswordReset" }, context.SecurityRevocationReason);
            Assert.Equal(2, audit.Count);
            if (action != "reset")
            {
                Assert.True(locks.Count >= 2, "The old serializable snapshot must be retried, not committed.");
                Assert.Contains(PostgresErrorCodes.SerializationFailure, failures);
                if (observeSnapshot) Assert.Contains(snapshots, x => x.EndsWith("visibleContexts=1", StringComparison.Ordinal));
            }
            var revoked = Assert.Single(audit, x => x.Action == "ContextSecurityRevoked");
            Assert.Equal(context.Id, revoked.WorkContextId); Assert.Equal(TestAccounts.AdminId, revoked.ActorUserId);
            Assert.NotNull(revoked.BeforeJson); Assert.NotEmpty(revoked.AfterJson);
            if (action == "deactivate") Assert.True((await users.SetActiveAsync(TestAccounts.Ipqa1Id, true, TestAccounts.AdminId, "admin", default))!.IsActive);
            if (action == "role") Assert.Equal("IPQA", (await users.UpdateAsync(TestAccounts.Ipqa1Id, new("Synthetic inspector", "IPQA"), TestAccounts.AdminId, "admin", default))!.Role);
            var preflight = await f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued));
            Assert.Equal(HttpStatusCode.Conflict, preflight.StatusCode);
            Assert.Contains("SourceSecurityRevoked", await preflight.Content.ReadAsStringAsync());
            Assert.Equal(2, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
        }
        finally
        {
            f.Fault.Release.TrySetResult(); releaseSnapshot.TrySetResult(); f.Fault.ObserveAsync = null; f.Fault.ObserveFailure = null;
            // Release any blocked command before fixture teardown; never replace a primary assertion.
            try { await System.Threading.Tasks.Task.WhenAll(new Task?[] { issue, security }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* Test body owns the success assertions; cleanup must not mask its failure. */ }
        }
    }

    internal static async Task FamilyBeforeIssuance(PersonalSessionFixture f, bool logout, ITestOutputHelper output)
    {
        var task = await Task(f); var a = await f.Login(); var original = await Issue(f, task, a); var b = await f.Login("dev-ipqa-2");
        using var scope = f.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<LocalAuthService>();
        using var issueScope = f.Factory.Services.CreateScope();
        var contexts = issueScope.ServiceProvider.GetRequiredService<WorkContextService>();
        var holder = 0;
        var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pause = logout ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"RefreshTokenSessions\"";
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection c)
            {
                if (command.CommandText.Contains(pause, StringComparison.Ordinal)) holder = c.ProcessID;
                if (holder != 0 && command.CommandText.Contains("FROM \"ApplicationUsers\"", StringComparison.Ordinal) &&
                    command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
                { output.WriteLine($"waiting issuer isolation={command.Transaction!.IsolationLevel}"); waiter.TrySetResult(c.ProcessID); }
            }
            return System.Threading.Tasks.Task.CompletedTask;
        };
        f.Fault.Arm(pause);
        Task? change = null; Task<WorkContextIssued>? issue = null;
        try
        {
            change = logout ? auth.LogoutAsync(TestAccounts.Ipqa1Id, a.PersonalSessionId, a.RefreshToken, default) :
                f.Refresh(a.RefreshToken!);
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            issue = contexts.IssueAsync(task, Input(f), Principal(a), default);
            await WaitForBlocker(f, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10)), holder);
            f.Fault.Release.TrySetResult();
            await change;
            var error = await Record.ExceptionAsync(async () => await issue);
            f.Fault.ObserveAsync = null;
            var live = await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == a.PersonalSessionId && x.RevokedAtUtc == null));
            var count = await f.Read(db => db.WorkContexts.CountAsync());
            output.WriteLine($"logout={logout}, liveFamilyRows={live}, contexts={count}, issueError={error?.GetType().Name ?? "none"}");
            Assert.Equal(logout ? 0 : 1, live);
            if (logout) Assert.Equal(401, Assert.IsType<PersonalAccessException>(error).Status);
            else Assert.Null(error);
            Assert.Equal(logout ? 1 : 2, count);
            Assert.Equal(logout ? 1 : 2, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
            Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync(x => x.SecurityRevokedAtUtc != null)));
            Assert.Equal(1, await f.Read(db => db.ApplicationUsers.Where(x => x.Id == TestAccounts.Ipqa1Id).Select(x => x.AuthVersion).SingleAsync()));
            Assert.Equal(HttpStatusCode.OK, (await f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, original))).StatusCode);
        }
        finally
        {
            f.Fault.Release.TrySetResult(); f.Fault.ObserveAsync = null;
            try { await System.Threading.Tasks.Task.WhenAll(new[] { change, issue }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* Preserve the primary assertion while releasing test resources. */ }
        }
    }

    private static async Task AssertKeyShareCompatible(PersonalSessionFixture f)
    {
        await using var connection = new NpgsqlConnection(f.Connection); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1 FROM \"ApplicationUsers\" WHERE \"Id\" = @user FOR KEY SHARE NOWAIT", connection);
        command.Parameters.AddWithValue("user", TestAccounts.Ipqa1Id);
        Assert.Equal(1, await command.ExecuteScalarAsync());
    }

    private static async Task WaitForBlocker(PersonalSessionFixture f, int waiter, int blocker)
    {
        await using var monitor = new NpgsqlConnection(f.Connection); await monitor.OpenAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            await using var query = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiter))", monitor);
            query.Parameters.AddWithValue("blocker", blocker); query.Parameters.AddWithValue("waiter", waiter);
            if (await query.ExecuteScalarAsync(deadline.Token) is true) return;
            await System.Threading.Tasks.Task.Delay(10, deadline.Token); // Poll an observed lock condition, not a scheduling guess.
        }
    }
}
