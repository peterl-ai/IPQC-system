using System.Security.Claims;

namespace JaxPower.Ipqc.Api.Security;

public static class IpqcPermissions
{
    public const string StandardsRead = "PatrolStandards.Read";
    public const string StandardsWrite = "PatrolStandards.Write";
    public const string PlansRead = "PatrolPlans.Read";
    public const string PlansWrite = "PatrolPlans.Write";
    public const string TasksReview = "PatrolTasks.Review";
    public const string TasksExecuteOwn = "PatrolTasks.ExecuteOwn";
    public const string RecordsRead = "CompletedRecords.Read";
    public const string RecordsExport = "CompletedRecords.Export";
    public const string UsersRead = "Users.Read";
    public const string UsersWrite = "Users.Write";
    public const string ClaimType = "ipqc_permission";

    public static readonly string[] All = [StandardsRead, StandardsWrite, PlansRead, PlansWrite,
        TasksReview, TasksExecuteOwn, RecordsRead, RecordsExport, UsersRead, UsersWrite];
    private static readonly string[] Business = [StandardsRead, StandardsWrite, PlansRead, PlansWrite,
        TasksReview, RecordsRead, RecordsExport];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        "Admin" => [.. Business, UsersRead, UsersWrite],
        "PQE" => Business,
        "IPQA" => [TasksExecuteOwn, RecordsRead, RecordsExport],
        _ => []
    };

    public static bool Has(ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(ClaimType, permission);

    public static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static string Username(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name)!;
}
