using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPolicyReadTests
{
    [Fact] public async Task Review_definition_and_policy_preconditions_are_one_locked_read() {
        await using var f = await PersonalSessionFixture.CreateAsync(); await Review(f);
    }
    internal static async Task Review(PersonalSessionFixture f) {
        var id = await LineSharedPlanningFixture.Standard(f); var pqe = await f.Login("pqe"); var admin = await f.Login("admin");
        var old = (await f.Client(admin.AccessToken).GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{id}"))!;
        var definition = PhotoPolicyTests.Edit(old) with { PatrolStandardName = "Changed concurrent Standard" };
        var expected = await f.Read(async db => {
            var s = await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems).SingleAsync(x => x.Id == id);
            return s.InspectionItems.ToDictionary(i => i.Id, i => DraftCanonical.Hash(PhotoPolicyService.Definition(s, i)));
        });
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains("FROM \"PhotoPolicyStates\"", StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm("FROM \"PhotoPolicyStates\"");
        var reading = PhotoPolicyFixture.Read(f, id, pqe); Task<HttpResponseMessage>? editing = null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            editing = f.Client(admin.AccessToken).PutAsJsonAsync($"/api/patrol-standards/{id}", definition);
            if (f.PostgresRoot is not null) await PostgresBlocking.Wait(f.Connection, holder, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        } finally { f.Fault.Release.TrySetResult(); }
        var result = await reading; (await editing!).EnsureSuccessStatusCode();
        Assert.NotNull(result.ReviewStandard); Assert.Equal(old, result.ReviewStandard with { InspectionItems = old.InspectionItems });
        Assert.Equal(old.InspectionItems, result.ReviewStandard.InspectionItems);
        Assert.All(result.Items, i => { Assert.Equal(expected[i.StandardItemId], i.DefinitionHash); Assert.NotNull(i.Confirmation); });
        var after = await PhotoPolicyFixture.Read(f, id, pqe);
        Assert.Equal("Changed concurrent Standard", after.ReviewStandard!.PatrolStandardName);
        Assert.All(after.Items, i => { Assert.Null(i.Confirmation); Assert.NotEqual(expected[i.StandardItemId], i.DefinitionHash); });
    }
}
