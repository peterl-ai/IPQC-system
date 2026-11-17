using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed record NgPicSetup(DraftSetup S, AuthOutput Admin, List<PicRevisionReceipt> Pics, DraftOperationReceipt? Sample, WorkContextIssued SourceB);
internal static class NgPicSelectionFixture
{
    internal const string Previous = "20261117161849_R3205IndependentPicRoster";
    internal const string Current = "20261117191540_R3205NgPicDraftSelections";
    internal static string Url(DraftSetup s) => s.Root + "/ng-pic-selection-batches";
    internal static string ReadUrl(DraftSetup s) => s.Root + "/shared-draft/ng-pics";
    internal static string ReceiptUrl(DraftSetup s, Guid id) => s.Root + $"/ng-pic-selections/{id}/receipt";
    internal static async Task<NgPicSetup> Setup(PersonalSessionFixture f, bool quantitative = false, bool sampled = true) {
        var s = await SharedDraftFixture.Setup(f, quantitative, sampleCount: "3");
        var sample = sampled ? await DraftEvidenceAssessmentTests.Sample(s, value: quantitative ? 3m : null) : null;
        var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B); var admin = await f.Login("admin"); var pics = new List<PicRevisionReceipt>();
        foreach (var i in new[] { 1, 2 }) pics.Add((await PicRosterTests.Accepted(await f.Client(admin.AccessToken).PostAsJsonAsync(PicRosterTests.Root, PicRosterTests.Input("Synthetic PIC " + i, "Synthetic Dept " + i)))).Receipt);
        return new(s, admin, pics, sample, sourceB);
    }
    internal static NgPicBatchInput Input(NgPicSetup u, long version = 1, IEnumerable<Guid>? ids = null, NgPicSelectionReceipt? previous = null, WorkContextIssued? source = null) {
        var s = u.S; var c = source ?? s.Source;
        return new(Guid.NewGuid(), 1, s.F.Line1, 2, s.F.Options.ValidEnvironment!, version,
            new(Guid.NewGuid(), 1, previous is null ? "SelectNgPics" : "CorrectNgPics", s.ItemId, s.AttemptId, c.Context.Id, c.Context.UserId, version, 1,
                (ids ?? [u.Pics[0].PicId]).ToList(), previous is null ? null : new(previous.OperationId, previous.ContentHash)), new(c.Context.Id, c.ProofSecret));
    }
    internal static async Task<NgPicBatchResult> Accept(NgPicSetup u, NgPicBatchInput b, AuthOutput? actor = null) {
        var r = await u.S.Client(actor).PostAsJsonAsync(Url(u.S), b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString()); var value = (await r.Content.ReadFromJsonAsync<NgPicBatchResult>())!;
        Assert.False(value.CanExecute); Assert.False(value.CanSubmit); Assert.False(value.AcceptsFormalInspection); return value;
    }
    internal static async Task<NgPicReadOutput> Read(NgPicSetup u, string query = "") {
        var r = await u.S.Client().GetAsync(ReadUrl(u.S) + query); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        var value = (await r.Content.ReadFromJsonAsync<NgPicReadOutput>())!; Assert.False(value.CanExecute); Assert.False(value.CanSubmit);
        Assert.All(value.Items, i => { Assert.False(i.IsRequired); Assert.False(i.BlocksSubmit); }); return value;
    }
    internal static void EqualReceipt(NgPicSelectionReceipt a, NgPicSelectionReceipt b) => Assert.Equal(JsonSerializer.Serialize(a, DraftCanonical.Json), JsonSerializer.Serialize(b, DraftCanonical.Json));
    internal static async Task<string> Facts(PersonalSessionFixture f) => await PicRosterTests.Facts(f) + await f.Read(async db => JsonSerializer.Serialize(new {
        Operations = await db.NgPicSelectionOperations.AsNoTracking().OrderBy(x => x.TaskId).ThenBy(x => x.OperationId).ToListAsync(),
        Members = await db.NgPicSelectionMembers.AsNoTracking().OrderBy(x => x.TaskId).ThenBy(x => x.OperationId).ThenBy(x => x.PicId).ToListAsync(),
        Projections = await db.NgPicSelectionProjections.AsNoTracking().OrderBy(x => x.TaskId).ThenBy(x => x.TaskItemId).ThenBy(x => x.InspectionAttemptId).ToListAsync()
    }));
}
