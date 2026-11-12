using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class InspectionAttemptEndpoints
{
    public static void MapInspectionAttempts(this WebApplication app)
    {
        var reads = app.MapGroup("/api/line-patrol/tasks/{taskId:guid}/inspection-attempts");
        PersonalAccess.RequireCurrent(reads.MapGet("", async (Guid taskId, Guid? expectedTaskLineId,
            InspectionAttemptService service, HttpContext h, CancellationToken ct) =>
            Results.Ok(await service.ReadAsync(taskId, expectedTaskLineId, h.User, ct))), true);
        PersonalAccess.RequireCurrent(reads.MapGet("/{attemptId:guid}", async (Guid taskId, Guid attemptId,
            InspectionAttemptService service, HttpContext h, CancellationToken ct) =>
            Results.Ok(await service.ReadAttemptAsync(taskId, attemptId, h.User, ct))), true);
        var maintenance = app.MapGroup("/api/admin/inspection-identities");
        PersonalAccess.RequireCurrent(maintenance.MapGet("", async (int? page, int? pageSize,
            InspectionAttemptService service, HttpContext h, CancellationToken ct) =>
            Results.Ok(await service.InventoryAsync(page ?? 1, pageSize ?? 20, h.User, ct))), false)
            .RequireAuthorization(IpqcPermissions.UsersRead);
        PersonalAccess.RequireCurrent(maintenance.MapPost("/{taskId:guid}/initialize", async (Guid taskId,
            InspectionIdentityInitializeInput input, InspectionAttemptService service, HttpContext h, CancellationToken ct) =>
            Results.Ok(await service.InitializeAsync(taskId, input, h.User, ct))), false)
            .RequireAuthorization(IpqcPermissions.UsersWrite).WithMetadata(new WorkContextLimit(false));
    }
}
