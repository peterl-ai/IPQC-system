using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class LineSharedPlanningFixture
{
    public static async Task<Guid> Standard(PersonalSessionFixture f, int items = 2)
    {
        var id = Guid.NewGuid();
        await f.Edit(db =>
        {
            db.PatrolStandards.Add(new PatrolStandard { Id = id, PatrolStandardName = "Synthetic shared standard", LineName = "Untrusted text line",
                FactoryCode = "Synthetic factory", UpdatedAtUtc = f.Clock.Now.UtcDateTime, CreatedAtUtc = f.Clock.Now.UtcDateTime,
                InspectionItems = Enumerable.Range(1, items).Select(i => new PatrolStandardItem { Id = Guid.NewGuid(), SequenceNo = i,
                    InspectionItem = $"Synthetic item {i}", InspectionType = "Qualitative", PhotoRequirement = "FutureRawPolicy" }).ToList() });
            return Task.CompletedTask;
        }); return id;
    }
    public static SharedPlanInput Input(PersonalSessionFixture f, Guid standard, Guid? line = null, params string[] rounds) => new(null,
        "Synthetic shared plan", standard, line ?? f.Line1, rounds.Length > 0 ? rounds.ToList() : ["Day", "Night"],
        "2026-01-01T00:00", null, true);
    public static async Task<HttpClient> Admin(PersonalSessionFixture f, string name = "admin") => f.Client((await f.Login(name)).AccessToken);
    public static async Task<SharedPlanOutput> Plan(PersonalSessionFixture f, Guid standard, Guid? line = null, params string[] rounds)
    {
        var response = await (await Admin(f)).PostAsJsonAsync("/api/line-patrol-plans", Input(f, standard, line, rounds));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SharedPlanOutput>())!;
    }
    public static async Task<int> Generate(PersonalSessionFixture f)
    {
        using var scope = f.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>().GenerateDueAsync();
    }
    public static async Task<List<PatrolTask>> Tasks(PersonalSessionFixture f) => await f.Read(db => db.PatrolTasks.AsNoTracking().Include(x => x.Items).ToListAsync());
    public static async Task<SharedPlanOutput> Toggle(PersonalSessionFixture f, SharedPlanOutput p, bool enabled)
    {
        var response = await (await Admin(f)).PostAsJsonAsync($"/api/line-patrol-plans/{p.Id}/{(enabled ? "enable" : "disable")}", new SharedPlanRevisionInput(p.SharedPlanRevision!.Value));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SharedPlanOutput>())!;
    }
    public static SharedPlanInput Edit(SharedPlanOutput p) => new(p.PlanNo, p.PlanName, p.PatrolStandardId, p.LineId!.Value,
        p.Rounds, p.EffectiveStartLocal, p.EffectiveEndLocal, p.IsEnabled, p.SharedPlanRevision);
    public static async Task<SharedPlanOutput> Save(PersonalSessionFixture f, SharedPlanOutput p, SharedPlanInput input)
    {
        var response = await (await Admin(f)).PutAsJsonAsync($"/api/line-patrol-plans/{p.Id}", input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SharedPlanOutput>())!;
    }
}
