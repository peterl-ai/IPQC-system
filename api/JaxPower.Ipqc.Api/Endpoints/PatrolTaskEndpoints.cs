using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class PatrolTaskEndpoints
{
    public static void MapPatrolTasks(this WebApplication app)
    {
        var group = app.MapGroup("/api/patrol-tasks");
        group.MapGet("", async (string? taskNo, string? status, Guid? planId, Guid? standardId,
            string? factory, string? line, string? inspector, DateTime? scheduledFrom, DateTime? scheduledTo,
            DateTime? submittedFrom, DateTime? submittedTo, int? page, int? pageSize,
            PatrolTaskExecutionService service, HttpContext http, CancellationToken ct) =>
        {
            if (!CanReview(http)) return Forbidden();
            var number = page ?? 1;
            var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Page must be >= 1 and pageSize must be 1–100."] });
            return Results.Ok(await service.ListAsync(taskNo, status, planId, standardId, factory, line, inspector,
                scheduledFrom, scheduledTo, submittedFrom, submittedTo, null, number, size, ct));
        });
        group.MapGet("/my", async (string? status, int? page, int? pageSize,
            PatrolTaskExecutionService service, HttpContext http, CancellationToken ct) =>
        {
            var assignee = Assignee(http);
            if (assignee is null) return Forbidden();
            if (status is not (null or "PendingInspection" or "InProgress" or "Rejected"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use a task execution queue status."] });
            var number = page ?? 1;
            var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Page must be >= 1 and pageSize must be 1–100."] });
            return Results.Ok(await service.ListAsync(null, status ?? "Active", null, null, null, null, null,
                null, null, null, null, assignee, number, size, ct));
        });
        group.MapGet("/{id:guid}", async (Guid id, PatrolTaskExecutionService service, HttpContext http, CancellationToken ct) =>
        {
            var task = await service.GetAsync(id, ct);
            if (task is null) return Results.Problem(statusCode: 404, title: "Patrol Task not found.");
            if (!CanReview(http) && Assignee(http) != task.Summary.AssignedInspectorKey) return Forbidden();
            return Results.Ok(task);
        });
        group.MapPut("/{id:guid}/draft", async (Guid id, TaskDraftInput input, PatrolTaskExecutionService service,
            HttpContext http, CancellationToken ct) =>
        {
            var assignee = Assignee(http);
            if (assignee is null) return Forbidden();
            try
            {
                return await service.SaveDraftAsync(id, assignee, input, ct) is { } task ? Results.Ok(task)
                    : Results.Problem(statusCode: 404, title: "Patrol Task not found.");
            }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        });
        group.MapPost("/{id:guid}/submit", async (Guid id, PatrolTaskExecutionService service,
            HttpContext http, CancellationToken ct) =>
        {
            var assignee = Assignee(http);
            if (assignee is null) return Forbidden();
            try
            {
                return await service.SubmitAsync(id, assignee, ct) is { } task ? Results.Ok(task)
                    : Results.Problem(statusCode: 404, title: "Patrol Task not found.");
            }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        });
        group.MapPost("/{id:guid}/approve", async (Guid id, PatrolTaskReviewService service,
            HttpContext http, CancellationToken ct) =>
        {
            if (!CanReview(http)) return Forbidden();
            try { await service.ApproveAsync(id, Role(http), ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        });
        group.MapPost("/{id:guid}/reject", async (Guid id, RejectTaskInput input, PatrolTaskReviewService service,
            HttpContext http, CancellationToken ct) =>
        {
            if (!CanReview(http)) return Forbidden();
            try { await service.RejectAsync(id, Role(http), input.Reason, ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        });
        group.MapPost("/batch-approve", async (BatchApproveInput input, PatrolTaskReviewService service,
            HttpContext http, CancellationToken ct) =>
        {
            if (!CanReview(http)) return Forbidden();
            try { await service.BatchApproveAsync(input.TaskIds, Role(http), ct); return Results.NoContent(); }
            catch (Exception ex) when (IsWorkflowError(ex)) { return Error(ex); }
        });
    }

    private static string Role(HttpContext http) => http.Request.Headers["X-Dev-Role"].ToString();
    private static bool CanReview(HttpContext http) => Role(http) is "admin" or "pqe";
    private static string? Assignee(HttpContext http)
    {
        var key = http.Request.Headers["X-Dev-Assignee-Key"].ToString();
        return Role(http) == "ipqa" && PatrolPlanService.DevelopmentAssignees.Any(x => x.Key == key) ? key : null;
    }
    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Role or Task assignment is not authorized.");
    private static bool IsWorkflowError(Exception ex) => ex is TaskValidationException or TaskWorkflowException
        or UnauthorizedAccessException or KeyNotFoundException;
    private static IResult Error(Exception ex) => ex switch
    {
        TaskValidationException validation => Results.ValidationProblem(validation.Errors),
        TaskWorkflowException conflict => Results.Problem(statusCode: 409, title: conflict.Message),
        UnauthorizedAccessException => Forbidden(),
        KeyNotFoundException => Results.Problem(statusCode: 404, title: "Patrol Task not found."),
        _ => throw ex
    };
}
