using System.Threading.RateLimiting;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Http.Features;

namespace JaxPower.Ipqc.Api.Endpoints;

public sealed record WorkContextLimit(bool Issue);

// Singleton partitioned limiter is invoked AFTER current-family middleware, before body binding.
public sealed class WorkContextLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = key.StartsWith("issue:", StringComparison.Ordinal) ? 10 : 30,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    public RateLimitLease Acquire(string key) => limiter.AttemptAcquire(key);
    public void Dispose() => limiter.Dispose();
}

public static class WorkContextEndpoints
{
    public static void MapWorkContexts(this WebApplication app)
    {
        var personal = app.MapGroup("/api/line-patrol/tasks/{taskId:guid}/work-contexts");
        PersonalAccess.RequireCurrent(personal.MapPost("", async (Guid taskId, WorkContextIssueInput input,
            WorkContextService s, HttpContext h, CancellationToken ct) => {
                var result = await s.IssueAsync(taskId, input, h.User, ct);
                return Results.Created($"/api/line-patrol/tasks/{taskId}/work-contexts/{result.Context.Id}", result);
            }), true).WithMetadata(new WorkContextLimit(true));
        PersonalAccess.RequireCurrent(personal.MapGet("/{contextId:guid}", async (Guid taskId, Guid contextId,
            WorkContextService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.ReadAsync(contextId, taskId, h.User, ct))), true);
        PersonalAccess.RequireCurrent(personal.MapPost("/validate-sources", async (Guid taskId, WorkContextPreflightInput input,
            WorkContextService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.PreflightAsync(taskId, input, h.User, ct))), true)
            .WithMetadata(new WorkContextLimit(false));
        var admin = app.MapGroup("/api/admin/work-contexts");
        PersonalAccess.RequireCurrent(admin.MapGet("/{contextId:guid}", async (Guid contextId,
            WorkContextService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.ReadAsync(contextId, null, h.User, ct))), false)
            .RequireAuthorization(IpqcPermissions.UsersRead);
        PersonalAccess.RequireCurrent(admin.MapGet("/{contextId:guid}/audit", async (Guid contextId, int? page, int? pageSize,
            WorkContextService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.AuditAsync(contextId, page ?? 1, pageSize ?? 20, h.User, ct))), false)
            .RequireAuthorization(IpqcPermissions.UsersRead);
        PersonalAccess.RequireCurrent(admin.MapPost("/{contextId:guid}/security-revoke", async (Guid contextId, WorkContextRevokeInput input,
            WorkContextService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.RevokeAsync(contextId, input, h.User, ct))), false)
            .RequireAuthorization(IpqcPermissions.UsersWrite).WithMetadata(new WorkContextLimit(false));
    }

    public static async Task Limits(HttpContext h, RequestDelegate next)
    {
        if (h.GetEndpoint()?.Metadata.GetMetadata<WorkContextLimit>() is { } policy)
        {
            using var lease = h.RequestServices.GetRequiredService<WorkContextLimits>()
                .Acquire((policy.Issue ? "issue:" : "preflight:") + h.User.FindFirst("sub")?.Value);
            if (!lease.IsAcquired) { h.Response.StatusCode = 429; return; }
            if (h.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                feature.MaxRequestBodySize = 32768;
            if (h.Request.ContentLength > 32768) { h.Response.StatusCode = 413; return; }
            // Also bound unknown-length/chunked bodies and test hosts without the server feature.
            var original = h.Request.Body;
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int read;
            while ((read = await original.ReadAsync(bytes, h.RequestAborted)) != 0)
            {
                if (buffer.Length + read > 32768) { h.Response.StatusCode = 413; return; }
                buffer.Write(bytes, 0, read);
            }
            buffer.Position = 0;
            h.Request.Body = buffer;
            try { await next(h); }
            finally { h.Request.Body = original; }
            return;
        }
        await next(h);
    }
}
