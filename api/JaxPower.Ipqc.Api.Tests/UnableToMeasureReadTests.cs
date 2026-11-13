using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasureReadTests
{
    [Fact] public async Task Actual_frozen_minimum_survives_all_disposition_changes() { await using var p = await PhotoFixture.Create(); await Minimum(p); }
    internal static async Task Minimum(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F, sampleCount: "5"); var bytes = PhotoFixture.Image(); var a = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); a = await PhotoFixture.Ready(s, a.AttachmentId, bytes);
        var reference = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a))).Operations); var u = new UnableSetup(s, a, reference);
        var before = await ItemRemovalTests.Retained(p.F); var first = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation;
        Assert.Equal(5, first.MinimumSampleCount);
        var correction = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 2, kind: "CorrectUnableToMeasureNG", previous: first))).Operation; Assert.Equal(5, correction.MinimumSampleCount);
        var clear = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 3, kind: "ClearUnableToMeasureNG", previous: correction))).Operation; Assert.Equal(5, clear.MinimumSampleCount);
        Assert.Equal(before, await ItemRemovalTests.Retained(p.F)); Assert.All((await s.Read())!.CurrentAttempts, x => Assert.Equal(5, x.MinimumSampleCount));
        Assert.Equal(0, await p.F.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.ContributionOperations.CountAsync()));
    }
    [Fact] public async Task History_is_bounded_and_latest_projection_independent() { await using var p = await PhotoFixture.Create(); await Paging(p); }
    internal static async Task Paging(PhotoFixture p) {
        var u = await UnableToMeasureTests.Setup(p); UnableToMeasureReceipt? prior = null;
        for (var i = 0; i < 101; i++) {
            var b = UnableToMeasureTests.Input(u, 1 + i, kind: i == 0 ? "SetUnableToMeasureNG" : "CorrectUnableToMeasureNG", previous: prior, reason: "Documented obstruction " + i);
            await using var scope = p.F.Factory.Services.CreateAsyncScope(); prior = (await scope.ServiceProvider.GetRequiredService<SharedDraftService>().AcceptUnableAsync(u.S.TaskId, b, WorkContextFixture.Principal(u.S.B), default)).Operation;
        }
        var page = await UnableToMeasureTests.Read(u); Assert.Equal(100, page.History.Count); Assert.Equal(3, page.HistoryBeforeDraftVersion); Assert.Equal(102, page.DraftVersion);
        Assert.Equal(prior, page.Items.Single(x => x.TaskItemId == u.S.ItemId).LastOperation);
        var older = (await u.S.Client().GetFromJsonAsync<DraftDispositionOutput>(u.S.Root + "/shared-draft/item-dispositions?beforeDraftVersion=" + page.HistoryBeforeDraftVersion))!;
        Assert.Equal(2, Assert.Single(older.History).DraftVersion); Assert.Null(older.HistoryBeforeDraftVersion); Assert.Equal(prior, older.Items.Single(x => x.TaskItemId == u.S.ItemId).LastOperation);
        Assert.Equal(101, await p.F.Read(db => db.UnableToMeasureOperations.CountAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await u.S.Client().GetAsync(u.S.Root + "/shared-draft/item-dispositions?beforeDraftVersion=0")).StatusCode);
    }
}
