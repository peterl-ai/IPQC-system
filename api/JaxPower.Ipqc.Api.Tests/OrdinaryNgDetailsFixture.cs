using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class OrdinaryNgDetailsFixture
{
    internal const string PriorMigration = "20261117191540_R3205NgPicDraftSelections";
    internal const string CurrentMigration = "20261118210133_R3205OrdinaryNgDetailsDraft";
    internal static string WriteUrl(DraftSetup s) => s.Root + "/ordinary-ng-details-batches";
    internal static string ReadUrl(DraftSetup s) => s.Root + "/shared-draft/ordinary-ng-details";
    internal static string ReceiptUrl(DraftSetup s, Guid id) => s.Root + $"/ordinary-ng-details-operations/{id}/receipt";
    internal static string CommonUrl(DraftSetup s, Guid id) => s.Root + $"/operation-batches/{id}/receipt";

    internal static OrdinaryNgDetailsBatchInput Input(DraftSetup s, long version = 1, OrdinaryNgDetailsReceipt? prior = null,
        string? reason = "Measure outside range", string? type = null, string? remarks = null)
    {
        var context = s.Source.Context;
        return new(Guid.NewGuid(), 1, s.F.Line1, 2, PersonalSessionFixture.Environment, version,
            new(Guid.NewGuid(), 1, prior is null ? "SetOrdinaryNgDetails" : "CorrectOrdinaryNgDetails", s.ItemId, s.AttemptId,
                context.Id, context.UserId, version, version + 1, type, reason, remarks,
                prior is null ? null : new(prior.OperationId, prior.ContentHash)),
            new(context.Id, s.Source.ProofSecret));
    }

    internal static async Task<OrdinaryNgDetailsBatchResult> Accept(DraftSetup s, OrdinaryNgDetailsBatchInput request)
    {
        using var response = await s.Client().PostAsJsonAsync(WriteUrl(s), request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var result = (await response.Content.ReadFromJsonAsync<OrdinaryNgDetailsBatchResult>())!;
        Assert.False(result.CanExecute); Assert.False(result.CanSubmit); Assert.False(result.AcceptsFormalInspection);
        return result;
    }

    internal static async Task<OrdinaryNgDetailsReadOutput> Read(DraftSetup s, string query = "")
    {
        using var response = await s.Client().GetAsync(ReadUrl(s) + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var read = (await response.Content.ReadFromJsonAsync<OrdinaryNgDetailsReadOutput>())!;
        Assert.False(read.CanExecute); Assert.False(read.CanSubmit); Assert.False(read.AcceptsFormalInspection);
        Assert.All(read.Items, i => { Assert.False(i.IsRequired); Assert.False(i.BlocksSubmit); });
        return read;
    }

    internal static async Task<OrdinaryNgDetailsItem> Item(DraftSetup s, string query = "") =>
        (await Read(s, query)).Items.Single(x => x.TaskItemId == s.ItemId);

    internal static async Task Reject(DraftSetup s, OrdinaryNgDetailsBatchInput body, HttpStatusCode status, string code)
    {
        using var response = await s.Client().PostAsJsonAsync(WriteUrl(s), body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, json);
        Assert.Contains(code, json);
    }

    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, DraftCanonical.Json);

    internal static async Task<string> Facts(PersonalSessionFixture f) =>
        await NgPicSelectionFixture.Facts(f) + await f.Read(async db =>
            Json(await db.OrdinaryNgDetailsOperations.AsNoTracking().OrderBy(x => x.TaskId).ThenBy(x => x.DraftVersion).ToListAsync()));
}
