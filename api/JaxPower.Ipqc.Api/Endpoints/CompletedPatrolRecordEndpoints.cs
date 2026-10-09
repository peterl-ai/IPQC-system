using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class CompletedPatrolRecordEndpoints
{
    public static void MapCompletedPatrolRecords(this WebApplication app)
    {
        var group = app.MapGroup("/api/completed-patrol-records");
        group.MapGet("", async (string? taskNo, string? plan, string? standard, string? factory,
            string? line, string? inspector, string? shift, string? result, DateOnly? completedFrom,
            DateOnly? completedTo, int? page, int? pageSize, CompletedPatrolRecordService service,
            CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Page must be >= 1 and pageSize must be 1–100."] });
            if (completedFrom > completedTo || (completedTo is { } to && to == DateOnly.MaxValue))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["completedDate"] = ["Enter a valid completion date range."] });
            if (shift is not (null or "Day" or "Night") || result is not (null or "Qualified" or "Unqualified"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["filters"] = ["Use a valid Shift and Overall Result."] });
            return Results.Ok(await service.ListAsync(taskNo, plan, standard, factory, line, inspector,
                shift, result, completedFrom, completedTo, number, size, ct));
        });
        group.MapGet("/{id:guid}", async (Guid id, CompletedPatrolRecordService service, CancellationToken ct) =>
            await Report(id, service, ct));
        group.MapGet("/{id:guid}/report", async (Guid id, CompletedPatrolRecordService service, CancellationToken ct) =>
            await Report(id, service, ct));
        group.MapGet("/{id:guid}/report.xlsx", async (Guid id, CompletedPatrolRecordService service,
            CompletedPatrolReportExcel excel, CancellationToken ct) =>
        {
            try
            {
                var report = await service.GetReportAsync(id, ct);
                if (report is null) return Results.Problem(statusCode: 404, title: "Completed Patrol Record not found.");
                var safeTaskNo = new string(report.Summary.TaskNo.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
                return Results.File(excel.Write(report),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"IPQC-{safeTaskNo}.xlsx");
            }
            catch (CompletedRecordIntegrityException ex) { return Results.Problem(statusCode: 409, title: ex.Message); }
        });
    }

    private static async Task<IResult> Report(Guid id, CompletedPatrolRecordService service, CancellationToken ct)
    {
        try
        {
            var report = await service.GetReportAsync(id, ct);
            return report is null ? Results.Problem(statusCode: 404, title: "Completed Patrol Record not found.") : Results.Ok(report);
        }
        catch (CompletedRecordIntegrityException ex) { return Results.Problem(statusCode: 409, title: ex.Message); }
    }
}
