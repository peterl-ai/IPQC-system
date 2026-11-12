using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using JaxPower.Ipqc.Api.Contracts;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class WorkContextFixture
{
    public static async Task<Guid> Task(PersonalSessionFixture f, Guid? line = null)
    {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero);
        await LineSharedPlanningFixture.Plan(f, await LineSharedPlanningFixture.Standard(f), line);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        await LineSharedPlanningFixture.Generate(f);
        return Assert.Single(await LineSharedPlanningFixture.Tasks(f)).Id;
    }
    public static string Root(Guid task) => $"/api/line-patrol/tasks/{task}/work-contexts";
    public static WorkContextIssueInput Input(PersonalSessionFixture f) => new(f.Line1, 2, PersonalSessionFixture.Environment);
    public static async Task<WorkContextIssued> Issue(PersonalSessionFixture f, Guid task, AuthOutput auth)
    {
        var r = await f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<WorkContextIssued>())!;
    }
    public static WorkContextPreflightInput Sources(PersonalSessionFixture f, WorkContextIssued issued) =>
        new(f.Line1, 2, PersonalSessionFixture.Environment, [new(issued.Context.Id, issued.ProofSecret, issued.Context.UserId)]);
    public static ClaimsPrincipal Principal(AuthOutput auth, string role = "IPQA") => new(new ClaimsIdentity(
        new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).Claims.Append(new Claim(ClaimTypes.Role, role)), "test"));
}
