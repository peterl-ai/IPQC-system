using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

// Per-test endpoint wrapper: original metadata, middleware, body binding and handler remain intact.
// Waiting occurs AFTER real JWT/current-family admission and BEFORE any business writer is started.
internal sealed class AdmittedCasRace : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IExceptionHandler, IDisposable
{
    internal static readonly string[] Domains = ["binding", "sample", "removed", "photo", "unable"];
    private readonly IReadOnlyCollection<string> domains;
    private const string Matched = "Microsoft.AspNetCore.Routing.EndpointMatched";
    private readonly IDisposable listeners;
    private readonly ConcurrentBag<IDisposable> subscriptions = [];
    private readonly ConcurrentQueue<object> events = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> release = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> writerStarted = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<int>> locking = new();
    private readonly ConcurrentDictionary<string, int> transactions = new(), cas = new(), casRows = new(), commits = new();
    private readonly ConcurrentDictionary<string, byte> admitted = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly TaskCompletionSource all = Signal(), four = Signal();
    private readonly TaskCompletionSource held = Signal(), holdRelease = Signal();
    private IHttpContextAccessor? accessor;
    private int holdUsed, disposed;
    internal Guid Id { get; } = Guid.NewGuid();
    internal bool ObserveOnly { get; set; }
    internal bool DelayBindingAdmission { get; set; }
    internal Guid Actor { get; set; }
    internal Guid Family { get; set; }
    internal IReadOnlyDictionary<string, (Guid UserId, Guid SessionId)>? AuthorityByDomain { get; set; }
    internal string[] Secrets { get; set; } = [];
    internal int AdmittedCount => admitted.Count;
    internal int Transactions => transactions.Values.Sum();
    internal int CasAttempts => cas.Values.Sum();
    internal int CasCount(string domain) => cas.GetValueOrDefault(domain);
    internal int? CasRows(string domain) => casRows.TryGetValue(domain, out var rows) ? rows : null;
    internal int CommitCount(string domain) => commits.GetValueOrDefault(domain);
    internal Task AllAdmitted => all.Task;
    internal Task FourAdmitted => four.Task;
    internal Task AdmissionReadHeld => held.Task;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal AdmittedCasRace(IReadOnlyCollection<string>? domains = null) {
        this.domains = domains ?? Domains;
        foreach (var d in this.domains) { release[d] = Signal(); writerStarted[d] = Signal(); locking[d] = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        listeners = DiagnosticListener.AllListeners.Subscribe(this);
    }
    internal void Configure(IServiceCollection services) {
        services.AddHttpContextAccessor(); services.AddSingleton(this); services.AddSingleton<IExceptionHandler>(this);
        services.AddSingleton<IInterceptor>(sp => { accessor = sp.GetRequiredService<IHttpContextAccessor>(); return new Commands(this); });
        services.AddSingleton<IInterceptor>(new Connections(this)); services.AddSingleton<IInterceptor>(new TransactionsObserver(this));
    }
    private string? Domain(HttpContext? h) => h?.Request.Headers["X-IPQC-Cas-Race"].ToString() == Id.ToString() && domains.Contains(h.Request.Headers["X-IPQC-Cas-Domain"].ToString(), StringComparer.Ordinal)
        ? h.Request.Headers["X-IPQC-Cas-Domain"].ToString() : null;
    private string? CurrentDomain => Domain(accessor?.HttpContext);
    internal void Note(string stage, object? detail = null, string? domain = null) => events.Enqueue(new {
        Ms = elapsed.Elapsed.TotalMilliseconds, Stage = stage, Domain = domain ?? CurrentDomain,
        Trace = accessor?.HttpContext?.TraceIdentifier, Detail = detail
    });
    private object Error(Exception e) {
        string Safe(string s) { foreach (var secret in Secrets.Where(x => !string.IsNullOrEmpty(x))) s = s.Replace(secret, "[REDACTED]", StringComparison.Ordinal); return s; }
        return new { Type = e.GetType().FullName, Message = Safe(e.Message), Stack = Safe(e.StackTrace ?? ""),
            Primary = (e as SqliteException)?.SqliteErrorCode, Extended = (e as SqliteException)?.SqliteExtendedErrorCode,
            Inner = e.InnerException is null ? null : Error(e.InnerException),
            Aggregate = e is AggregateException a ? a.InnerExceptions.Select(Error).ToArray() : null };
    }
    public ValueTask<bool> TryHandleAsync(HttpContext h, Exception exception, CancellationToken ct) {
        if (Domain(h) is { } d) Note("UnhandledException", new { HttpTrace = h.TraceIdentifier, Exception = Error(exception) }, d);
        return ValueTask.FromResult(false); // Preserve the actual server's existing500 handling.
    }
    public void OnNext(DiagnosticListener source) {
        if (source.Name == "Microsoft.AspNetCore") subscriptions.Add(source.Subscribe(this, (name, _, _) => name == Matched));
    }
    public void OnNext(KeyValuePair<string, object?> item) {
        if (item.Key != Matched || item.Value is not HttpContext h || Domain(h) is not { } d || h.GetEndpoint() is not RouteEndpoint original || original.RequestDelegate is null) return;
        h.SetEndpoint(new RouteEndpoint(async context => {
            Assert.True(context.User.Identity?.IsAuthenticated == true, "CAS gate must follow JWT authentication");
            var authority = Assert.IsType<PersonalAuthority>(context.Items[typeof(PersonalAuthority)]);
            var expected = AuthorityByDomain is { } authors ? authors[d] : (UserId: Actor, SessionId: Family);
            Assert.Equal(expected.UserId, authority.UserId); Assert.Equal(expected.SessionId, authority.PersonalSessionId);
            Assert.True(admitted.TryAdd(d, 0), "Duplicate domain in one CAS cohort"); Note("Admitted", new { HttpTrace = context.TraceIdentifier }, d);
            if (admitted.Count >= 4) four.TrySetResult(); if (admitted.Count == domains.Count) all.TrySetResult();
            if (!ObserveOnly) await release[d].Task.WaitAsync(TimeSpan.FromSeconds(20), context.RequestAborted);
            await original.RequestDelegate(context);
        }, original.RoutePattern, original.Order, original.Metadata, original.DisplayName));
    }
    public void OnCompleted() { }
    public void OnError(Exception error) { }
    internal void Release(string d) { Note("Release", domain: d); release[d].TrySetResult(); }
    internal void ReleaseAll() { foreach (var d in domains) release[d].TrySetResult(); holdRelease.TrySetResult(); }
    internal Task WriterStarted(string domain) => writerStarted[domain].Task;
    internal Task<int> LockingPid(string domain) => locking[domain].Task;
    internal void ReleaseAdmissionRead() => holdRelease.TrySetResult();
    internal async Task<HttpResponseMessage> Send(HttpClient client, string d, string url, object input) {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(input, input.GetType()) };
        request.Headers.Add("X-IPQC-Cas-Race", Id.ToString()); request.Headers.Add("X-IPQC-Cas-Domain", d);
        Note("RequestStarted", domain: d);
        try { var r = await client.SendAsync(request); var text = await r.Content.ReadAsStringAsync(); string? code = null;
            try { using var json = JsonDocument.Parse(text); if (json.RootElement.TryGetProperty("code", out var value)) code = value.GetString(); } catch (JsonException) { }
            Note("Response", new { Status = (int)r.StatusCode, Code = code, SafeError = r.IsSuccessStatusCode ? null : Sanitize(text) }, d); return r;
        } catch (Exception e) { Note("ClientException", Error(e), d); throw; }
    }
    private string Sanitize(string value) { foreach (var secret in Secrets.Where(x => !string.IsNullOrEmpty(x))) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal); return value; }
    internal string Report() => JsonSerializer.Serialize(new { Id, Events = events.ToArray() });
    internal void Failure(string stage, Exception error) => Note(stage, Error(error));
    internal void Require(bool condition, string message) => Assert.True(condition, message + "\n" + Report());
    private sealed class Commands(AdmittedCasRace owner) : DbCommandInterceptor {
        private async Task Before(DbCommand c, CancellationToken ct) {
            if (owner.CurrentDomain is not { } d) return;
            if (!owner.admitted.ContainsKey(d)) {
                if (c.CommandText.Contains("FROM \"ApplicationUsers\"", StringComparison.Ordinal)) owner.Note("PreAdmissionUserRead", domain: d);
                if (c.CommandText.Contains("FROM \"RefreshTokenSessions\"", StringComparison.Ordinal)) owner.Note("PreAdmissionFamilyRead", domain: d);
            }
            if (owner.DelayBindingAdmission && d == "binding" && !owner.admitted.ContainsKey(d) && c.CommandText.Contains("FROM \"RefreshTokenSessions\"", StringComparison.Ordinal) && Interlocked.Exchange(ref owner.holdUsed, 1) == 0) {
                owner.Note("AdmissionReadHeld", domain: d); owner.held.TrySetResult(); await owner.holdRelease.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            if (c.CommandText.Contains("UPDATE \"SharedDrafts\"", StringComparison.Ordinal)) { owner.cas.AddOrUpdate(d, 1, (_, n) => n + 1); owner.Note("CasAttempt", domain: d); }
            if (c.Connection is NpgsqlConnection pg && c.Transaction is not null && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) owner.locking[d].TrySetResult(pg.ProcessID);
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { await Before(c, ct); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> result, CancellationToken ct = default) { await Before(c, ct); return result; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData e, int result, CancellationToken ct = default) {
            if (owner.CurrentDomain is { } d && c.CommandText.Contains("UPDATE \"SharedDrafts\"", StringComparison.Ordinal)) { owner.casRows[d] = result; owner.Note("CasRows", new { Rows = result }, d); } return ValueTask.FromResult(result);
        }
        public override Task CommandFailedAsync(DbCommand c, CommandErrorEventData e, CancellationToken ct = default) { if (owner.CurrentDomain is { } d) owner.Note("ProviderFailure", owner.Error(e.Exception), d); return Task.CompletedTask; }
    }
    private sealed class Connections(AdmittedCasRace owner) : DbConnectionInterceptor {
        public override Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default) {
            if (owner.CurrentDomain is { } d && owner.admitted.ContainsKey(d)) {
                owner.Note("PostAdmissionConnectionOpen", domain: d);
                // Observe the provider's real native BEGIN IMMEDIATE, not merely an opened connection.
                // Do not replace its busy handler, timeout, isolation or retry behavior. Never log SQL.
                if (c is SqliteConnection sqlite) SQLitePCL.raw.sqlite3_trace(sqlite.Handle, (SQLitePCL.strdelegate_trace)((_, sql) => {
                    if (sql.Trim().Equals("BEGIN IMMEDIATE;", StringComparison.OrdinalIgnoreCase)) {
                        owner.Note("NativeWriterBeginEntered", domain: d); owner.writerStarted[d].TrySetResult();
                    }
                }), null);
            }
            return Task.CompletedTask;
        }
        private static void Unhook(DbConnection c) { if (c is SqliteConnection sqlite) SQLitePCL.raw.sqlite3_trace(sqlite.Handle, (SQLitePCL.strdelegate_trace)null!, null); }
        public override InterceptionResult ConnectionClosing(DbConnection c, ConnectionEventData e, InterceptionResult result) { Unhook(c); return result; }
        public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection c, ConnectionEventData e, InterceptionResult result) { Unhook(c); return ValueTask.FromResult(result); }
    }
    private sealed class TransactionsObserver(AdmittedCasRace owner) : DbTransactionInterceptor {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection c, TransactionEndEventData e, DbTransaction result, CancellationToken ct = default) {
            if (owner.CurrentDomain is { } d) { owner.transactions.AddOrUpdate(d, 1, (_, n) => n + 1); owner.Note("TransactionAcquired", domain: d); } return ValueTask.FromResult(result);
        }
        public override ValueTask<DbTransaction> TransactionUsedAsync(DbConnection c, TransactionEventData e, DbTransaction result, CancellationToken ct = default) {
            if (owner.CurrentDomain is { } d) { owner.transactions.AddOrUpdate(d, 1, (_, n) => n + 1); owner.Note("TransactionAcquired", domain: d); } return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction t, TransactionEndEventData e, CancellationToken ct = default) {
            if (owner.CurrentDomain is { } d) { owner.commits.AddOrUpdate(d, 1, (_, n) => n + 1); owner.Note("Commit", domain: d); } return Task.CompletedTask;
        }
        public override Task TransactionFailedAsync(DbTransaction t, TransactionErrorEventData e, CancellationToken ct = default) { if (owner.CurrentDomain is { } d) owner.Note("TransactionFailure", owner.Error(e.Exception), d); return Task.CompletedTask; }
    }
    public void Dispose() {
        ReleaseAll(); if (Interlocked.Exchange(ref disposed, 1) != 0) return; listeners.Dispose(); foreach (var s in subscriptions) s.Dispose();
        if (Environment.GetEnvironmentVariable("IPQC_CAS_EVIDENCE") is { Length: > 0 } path) {
            var full = Path.GetFullPath(path); var temp = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test evidence must remain in temporary storage");
            Directory.CreateDirectory(full); File.WriteAllText(Path.Combine(full, $"race-{Id:N}.json"), Report());
        }
    }
}
