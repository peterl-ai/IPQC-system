using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class PhotoPolicyFixture
{
    public const string Previous = "20261113211600_R3205SampleTransitionEvidenceBindings";
    public static string Root(Guid standard) => $"/api/patrol-standards/{standard}/photo-policies";
    public static string ConfirmUrl(Guid standard, Guid item) => $"/api/patrol-standards/{standard}/items/{item}/photo-policy-confirmations";
    public static PhotoPolicyConfirmInput Input(PhotoPolicyItem item, string value = "Optional") =>
        new(Guid.NewGuid(), 1, item.DefinitionId, item.DefinitionHash, item.Confirmation?.ConfirmationId, value);
    public static async Task<PhotoPolicyPage> Read(PersonalSessionFixture f, Guid standard, AuthOutput actor) =>
        (await f.Client(actor.AccessToken).GetFromJsonAsync<PhotoPolicyPage>(Root(standard)))!;
    public static async Task<PhotoPolicyReceipt> Confirm(PersonalSessionFixture f, Guid standard, Guid item, PhotoPolicyConfirmInput input, AuthOutput actor) {
        var r = await f.Client(actor.AccessToken).PostAsJsonAsync(ConfirmUrl(standard, item), input);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<PhotoPolicyReceipt>())!;
    }
    public static async Task<bool> Available(PersonalSessionFixture f) => await f.Read(async db =>
        (await db.Database.GetAppliedMigrationsAsync()).Any(x => x.EndsWith("_R3205ConfirmedPhotoPolicies", StringComparison.Ordinal)));
    public static async Task ConfirmAll(PersonalSessionFixture f, Guid standard, string value = "Optional") {
        await using var scope = f.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<PhotoPolicyService>();
        var current = await f.Read(db => db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems).SingleAsync(x => x.Id == standard));
        if (await service.EffectiveAsync(current, default) is not null) return;
        var actor = await f.Login("pqe");
        var page = await service.ReadAsync(standard, WorkContextFixture.Principal(actor, "PQE"), default);
        foreach (var item in page.Items.Where(x => x.Confirmation is null))
            await service.ConfirmAsync(standard, item.StandardItemId, Input(item, value), WorkContextFixture.Principal(actor, "PQE"), default);
    }
    public static async Task<int> GenerateStrict(PersonalSessionFixture f) {
        await using var scope = f.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LineSharedTaskGenerationService>().GenerateDueAsync();
    }
}
