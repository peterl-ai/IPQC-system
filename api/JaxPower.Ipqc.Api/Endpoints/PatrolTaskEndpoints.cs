using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class PatrolTaskEndpoints
{
    public static void MapPatrolTasks(this WebApplication app)
    {
        var group = app.MapGroup("/api/patrol-tasks").RequireAuthorization();
        group.MapGet("", async (string? taskNo, string? status, Guid? planId, Guid? standardId,
            string? factory, string? line, string? inspector, DateTime? scheduledFrom, DateTime? scheduledTo,
            DateTime? submittedFrom, DateTime? submittedTo, int? page, int? pageSize,
            PatrolTaskExecutionService service, CancellationToken ct) =>
        {
            var number = page ?? 1; var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Invalid pagination."] });
            return Results.Ok(await service.ListAsync(taskNo, status, planId, standardId, factory, line,
                inspector, scheduledFrom, scheduledTo, submittedFrom, submittedTo, null, number, size, ct));
        }).RequireAuthorization(IpqcPermissions.TasksReview);
        group.MapGet("/my", async (string? status, int? page, int? pageSize,
            PatrolTaskExecutionService service, HttpContext http, CancellationToken ct) =>
        {
            if (status is not (null or "PendingInspection" or "InProgress" or "Rejected"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Invalid execution status."] });
            var number = page ?? 1; var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Invalid pagination."] });
            return Results.Ok(await service.ListAsync(null, status ?? "Active", null, null, null, null,
                null, null, null, null, null, IpqcPermissions.UserId(http.User), number, size, ct));
        }).RequireAuthorization(IpqcPermissions.TasksExecuteOwn);
        group.MapGet("/{id:guid}", async (Guid id, PatrolTaskExecutionService service,
            HttpContext http, CancellationToken ct) =>
        {
            if (!IpqcPermissions.Has(http.User, IpqcPermissions.TasksReview) &&
                (!IpqcPermissions.Has(http.User, IpqcPermissions.TasksExecuteOwn) ||
                 !await service.OwnsAsync(id, IpqcPermissions.UserId(http.User), ct))) return Forbidden();
            var task = await service.GetAsync(id, ct);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });
        group.MapPut("/{id:guid}/draft", async (Guid id, TaskDraftInput input,
            PatrolTaskExecutionService service, HttpContext http, CancellationToken ct) =>
        {
            try { return await service.SaveDraftAsync(id, IpqcPermissions.UserId(http.User), input, ct) is { } task
                ? Results.Ok(task) : Results.NotFound(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.TasksExecuteOwn);
        group.MapPost("/{id:guid}/submit", async (Guid id, PatrolTaskExecutionService service,
            HttpContext http, CancellationToken ct) =>
        {
            try { return await service.SubmitAsync(id, IpqcPermissions.UserId(http.User),
                IpqcPermissions.Username(http.User), ct) is { } task ? Results.Ok(task) : Results.NotFound(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.TasksExecuteOwn);
        group.MapPost("/{id:guid}/approve", async (Guid id, PatrolTaskReviewService service,
            HttpContext http, CancellationToken ct) =>
        {
            try { await service.ApproveAsync(id, IpqcPermissions.Username(http.User), ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.TasksReview);
        group.MapPost("/{id:guid}/reject", async (Guid id, RejectTaskInput input,
            PatrolTaskReviewService service, HttpContext http, CancellationToken ct) =>
        {
            try { await service.RejectAsync(id, IpqcPermissions.Username(http.User), input.Reason, ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.TasksReview);
        group.MapPost("/batch-approve", async (BatchApproveInput input, PatrolTaskReviewService service,
            HttpContext http, CancellationToken ct) =>
        {
            try { await service.BatchApproveAsync(input.TaskIds, IpqcPermissions.Username(http.User), ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.TasksReview);
    }

    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Task assignment is not authorized.");
    private static bool IsWorkflowError(Exception ex) => ex is TaskValidationException or TaskWorkflowException
        or UnauthorizedAccessException or KeyNotFoundException;
    private static IResult Error(Exception ex) => ex switch
    {
        TaskValidationException validation => Results.ValidationProblem(validation.Errors),
        TaskWorkflowException conflict => Results.Problem(statusCode: 409, title: conflict.Message),
        UnauthorizedAccessException => Forbidden(),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
