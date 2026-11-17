using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class PicRosterEndpoints
{
    public static void MapPicRoster(this WebApplication app) {
        var g = app.MapGroup("/api/admin/pic-roster").RequireAuthorization(IpqcPermissions.PicsRead);
        PersonalAccess.RequireCurrent(g.MapGet("", (bool? active, int? page, int? pageSize, PicRosterService s, HttpContext h, CancellationToken ct) =>
            s.ListAsync(h.User, active, page ?? 1, pageSize ?? 20, ct)), false);
        PersonalAccess.RequireCurrent(g.MapGet("/{id:guid}", (Guid id, PicRosterService s, HttpContext h, CancellationToken ct) => s.GetAsync(id, h.User, ct)), false);
        PersonalAccess.RequireCurrent(g.MapGet("/{id:guid}/history", (Guid id, int? page, int? pageSize, PicRosterService s, HttpContext h, CancellationToken ct) =>
            s.HistoryAsync(id, h.User, page ?? 1, pageSize ?? 20, ct)), false);
        PersonalAccess.RequireCurrent(g.MapGet("/requests/{requestId:guid}/receipt", (Guid requestId, PicRosterService s, HttpContext h, CancellationToken ct) =>
            s.RequestReceiptAsync(requestId, h.User, ct)), false);
        PersonalAccess.RequireCurrent(g.MapPost("", async (PicCreateInput input, PicRosterService s, HttpContext h, CancellationToken ct) => {
            var r = await s.CreateAsync(input, h.User, ct);
            return r.ReplayOnly ? Results.Ok(r) : Results.Created($"/api/admin/pic-roster/{r.Receipt.PicId}", r);
        }).RequireAuthorization(IpqcPermissions.PicsWrite).WithMetadata(new SharedDraftMutation()), false);
        PersonalAccess.RequireCurrent(g.MapPut("/{id:guid}", (Guid id, PicEditInput input, PicRosterService s, HttpContext h, CancellationToken ct) =>
            s.EditAsync(id, input, h.User, ct)).RequireAuthorization(IpqcPermissions.PicsWrite).WithMetadata(new SharedDraftMutation()), false);
        foreach (var active in new[] { true, false })
            PersonalAccess.RequireCurrent(g.MapPost(active ? "/{id:guid}/activate" : "/{id:guid}/deactivate", (Guid id, PicRevisionInput input, PicRosterService s, HttpContext h, CancellationToken ct) =>
                s.SetActiveAsync(id, input, active, h.User, ct)).RequireAuthorization(IpqcPermissions.PicsWrite).WithMetadata(new SharedDraftMutation()), false);
        PersonalAccess.RequireCurrent(app.MapGet("/api/line-patrol/tasks/{taskId:guid}/pic-roster", (Guid taskId, Guid? expectedTaskLineId, int? page, int? pageSize,
            PicRosterService s, HttpContext h, CancellationToken ct) => s.LookupAsync(taskId, expectedTaskLineId, h.User, page ?? 1, pageSize ?? 20, ct)), true);
    }
}
