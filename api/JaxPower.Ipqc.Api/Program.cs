using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Endpoints;
using JaxPower.Ipqc.Api.Services;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new AuthenticationClock(TimeProvider.System));
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
builder.Services.AddScoped<LocalAuthService>();
builder.Services.AddScoped<ApplicationUserService>();
builder.Services.AddScoped<PatrolStandardService>();
builder.Services.AddScoped<PatrolPlanService>();
builder.Services.AddScoped<PatrolTaskGenerationService>();
builder.Services.AddScoped<InspectionJudgmentService>();
builder.Services.AddScoped<PatrolTaskExecutionService>();
builder.Services.AddScoped<PatrolTaskReviewService>();
builder.Services.AddScoped<CompletedPatrolRecordService>();
builder.Services.AddSingleton<CompletedPatrolReportExcel>();
builder.Services.AddHostedService<PatrolSchedulerBackgroundService>();
builder.Services.AddSingleton<PatrolStandardExcel>();
var keyText = builder.Configuration["Authentication:JwtSigningKey"];
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing") &&
    (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32 ||
     keyText.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
     keyText.Contains("change-me", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Configure a strong Authentication:JwtSigningKey secret before starting production.");
var jwt = new JwtSettings { Key = string.IsNullOrWhiteSpace(keyText)
    ? RandomNumberGenerator.GetBytes(64) : Encoding.UTF8.GetBytes(keyText),
    AccessMinutes = builder.Configuration.GetValue("Authentication:AccessMinutes", 15),
    RefreshDays = builder.Configuration.GetValue("Authentication:RefreshDays", 14) };
if (jwt.AccessMinutes is < 1 or > 60 || jwt.RefreshDays is < 1 or > 14)
    throw new InvalidOperationException("Authentication lifetimes are out of range.");
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(jwt.Key),
        ValidateLifetime = false, ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents { OnTokenValidated = async context =>
    {
        var now = context.HttpContext.RequestServices.GetRequiredService<AuthenticationClock>().Clock.GetUtcNow().UtcDateTime;
        if (context.SecurityToken.ValidFrom > now.AddSeconds(30) ||
            context.SecurityToken.ValidTo <= now.AddSeconds(-30))
        { context.Fail("Token expired or not yet valid."); return; }
        var idText = context.Principal?.FindFirst("sub")?.Value;
        var versionText = context.Principal?.FindFirst("auth_version")?.Value;
        if (!Guid.TryParse(idText, out var id) || !int.TryParse(versionText, out var version))
        { context.Fail("Invalid identity."); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<IpqcDbContext>();
        var user = await db.ApplicationUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,
            context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive || user.AuthVersion != version)
        { context.Fail("Session revoked."); return; }
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        identity.AddClaim(new Claim("must_change_password", user.MustChangePassword ? "true" : "false"));
        if (!user.MustChangePassword)
            foreach (var permission in IpqcPermissions.ForRole(user.Role))
                identity.AddClaim(new Claim(IpqcPermissions.ClaimType, permission));
    }};
});
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IpqcPermissions.All)
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(IpqcPermissions.ClaimType, permission));
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.RejectionStatusCode = 429;
});
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuth();
app.MapUsers();

var group = app.MapGroup("/api/patrol-standards").RequireAuthorization(IpqcPermissions.StandardsRead);
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
    var row = await service.CreateAsync(input, IpqcPermissions.Username(http.User), ct);
    return Results.Created($"/api/patrol-standards/{row.Id}", row);
}).RequireAuthorization(IpqcPermissions.StandardsWrite);
group.MapPut("/{id:guid}", async (Guid id, StandardInput input, PatrolStandardService service,
    IpqcDbContext db, HttpContext http, CancellationToken ct) =>
{
    var errors = PatrolStandardService.Validate(input);
    if (await db.PatrolPlans.AnyAsync(x => x.PatrolStandardId == id && x.IsEnabled, ct) &&
        PatrolStandardService.ExecutabilityIssue(input) is { } issue)
        errors["InspectionItems"] = [issue];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    return await service.UpdateAsync(id, input, IpqcPermissions.Username(http.User), ct) is { } row
        ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Standard not found.");
}).RequireAuthorization(IpqcPermissions.StandardsWrite);
group.MapDelete("/{id:guid}", async (Guid id, PatrolStandardService service, IpqcDbContext db, CancellationToken ct) =>
{
    if (await db.PatrolPlans.AnyAsync(x => x.PatrolStandardId == id, ct) || await db.PatrolTasks.AnyAsync(x => x.PatrolStandardId == id, ct))
        return Results.Problem(statusCode: 409, title: "Patrol Standard is referenced by a plan or task.");
    return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.Problem(statusCode: 404, title: "Patrol Standard not found.");
}).RequireAuthorization(IpqcPermissions.StandardsWrite);
group.MapPost("/{id:guid}/copy", async (Guid id, CopyInput? input, PatrolStandardService service, HttpContext http, CancellationToken ct) =>
{
    if (input?.PatrolStandardName is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 200))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["PatrolStandardName"] = ["Patrol Standard Name must be 1–200 non-whitespace characters."] });
    return await service.CopyAsync(id, input?.PatrolStandardName, IpqcPermissions.Username(http.User), ct) is { } row
        ? Results.Created($"/api/patrol-standards/{row.Id}", row) : Results.Problem(statusCode: 404, title: "Patrol Standard not found.");
}).RequireAuthorization(IpqcPermissions.StandardsWrite);
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
    var row = await service.CreateAsync(input, IpqcPermissions.Username(http.User), ct);
    return Results.Created($"/api/patrol-standards/{row.Id}", row);
}).DisableAntiforgery().RequireAuthorization(IpqcPermissions.StandardsWrite);

app.MapPatrolPlans();
app.MapPatrolTasks();
app.MapCompletedPatrolRecords();

await UserBootstrap.InitializeAsync(app);
app.Run();

public partial class Program { }
