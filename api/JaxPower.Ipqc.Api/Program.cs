using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatrolStandardService>();
builder.Services.AddSingleton<PatrolStandardExcel>();
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("Ipqc") ?? throw new InvalidOperationException("ConnectionStrings:Ipqc is required.");
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing") &&
    !provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Production requires Database:Provider=Postgres.");
builder.Services.AddDbContext<IpqcDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)) options.UseNpgsql(connection);
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) options.UseSqlite(connection);
    else throw new InvalidOperationException("Database:Provider must be Sqlite or Postgres.");
});
builder.Services.AddCors(options => options.AddPolicy("LocalVite", policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("LocalVite");
}
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler();

// Phase 1C has no production authentication. Fail closed outside Development.
// In Development the role header is a test fixture, never a production identity.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/patrol-standards")) { await next(); return; }
    if (!app.Environment.IsDevelopment())
    {
        await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Authentication integration is not configured.").ExecuteAsync(context);
        return;
    }
    var role = context.Request.Headers["X-Dev-Role"].ToString();
    if (role is not ("admin" or "pqe"))
    {
        await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Patrol Standards require Admin or PQE.").ExecuteAsync(context);
        return;
    }
    await next();
});

var group = app.MapGroup("/api/patrol-standards");
group.MapGet("", async (string? standardName, string? factoryCode, string? lineCode, int? page, int? pageSize,
    PatrolStandardService service, CancellationToken ct) =>
{
    var number = page ?? 1;
    var size = pageSize ?? 10;
    if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Page must be >= 1 and pageSize must be 1–100."] });
    return Results.Ok(await service.ListAsync(standardName, factoryCode, lineCode, number, size, ct));
});
group.MapGet("/{id:guid}", async (Guid id, PatrolStandardService service, CancellationToken ct) =>
    await service.GetAsync(id, ct) is { } row ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Standard not found."));
group.MapPost("", async (StandardInput input, PatrolStandardService service, HttpContext http, CancellationToken ct) =>
{
    var errors = PatrolStandardService.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var row = await service.CreateAsync(input, http.Request.Headers["X-Dev-Role"].ToString(), ct);
    return Results.Created($"/api/patrol-standards/{row.Id}", row);
});
group.MapPut("/{id:guid}", async (Guid id, StandardInput input, PatrolStandardService service, HttpContext http, CancellationToken ct) =>
{
    var errors = PatrolStandardService.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    return await service.UpdateAsync(id, input, http.Request.Headers["X-Dev-Role"].ToString(), ct) is { } row
        ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Standard not found.");
});
group.MapDelete("/{id:guid}", async (Guid id, PatrolStandardService service, CancellationToken ct) =>
    await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.Problem(statusCode: 404, title: "Patrol Standard not found."));
group.MapPost("/{id:guid}/copy", async (Guid id, CopyInput? input, PatrolStandardService service, HttpContext http, CancellationToken ct) =>
{
    if (input?.PatrolStandardName is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 200))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["PatrolStandardName"] = ["Patrol Standard Name must be 1–200 non-whitespace characters."] });
    return await service.CopyAsync(id, input?.PatrolStandardName, http.Request.Headers["X-Dev-Role"].ToString(), ct) is { } row
        ? Results.Created($"/api/patrol-standards/{row.Id}", row) : Results.Problem(statusCode: 404, title: "Patrol Standard not found.");
});
const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
group.MapGet("/template", (PatrolStandardExcel excel) => Results.File(excel.Write(), contentType, "patrol-standard-template.xlsx"));
group.MapGet("/export", async (Guid id, PatrolStandardService service, PatrolStandardExcel excel, CancellationToken ct) =>
    await service.GetAsync(id, ct) is { } row ? Results.File(excel.Write(row), contentType, "patrol-standard.xlsx") : Results.Problem(statusCode: 404, title: "Patrol Standard not found."));
group.MapPost("/import", async (IFormFile file, PatrolStandardExcel excel, PatrolStandardService service,
    IConfiguration configuration, HttpContext http, CancellationToken ct) =>
{
    var limit = configuration.GetValue<long>("Excel:MaxUploadBytes", 5 * 1024 * 1024);
    if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || file.Length is <= 0 || file.Length > limit)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload a non-empty .xlsx file within the configured size limit."] });
    StandardInput input;
    try
    {
        await using var stream = file.OpenReadStream();
        input = excel.Read(stream);
    }
    catch (ExcelValidationException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [ex.Message] });
    }
    var errors = PatrolStandardService.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var row = await service.CreateAsync(input, http.Request.Headers["X-Dev-Role"].ToString(), ct);
    return Results.Created($"/api/patrol-standards/{row.Id}", row);
}).DisableAntiforgery();

app.Run();

public partial class Program { }
