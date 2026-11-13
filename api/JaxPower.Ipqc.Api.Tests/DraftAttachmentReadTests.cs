using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftAttachmentReadTests
{
    [Fact] public async Task History_is_bounded_without_losing_current_projection() { await using var p = await PhotoFixture.Create(); await Paging(p); }
    internal static async Task Paging(PhotoFixture p) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); DraftAttachmentReceipt? previous = null;
        for (var version = 0; version < 101; version++) {
            await using var scope = p.F.Factory.Services.CreateAsyncScope();
            var b = DraftAttachmentTests.Input(s, a, version, previous: previous, kind: version % 2 == 0 ? "AssociateAttachment" : "DetachAttachment");
            previous = Assert.Single((await scope.ServiceProvider.GetRequiredService<SharedDraftService>().AcceptAttachmentsAsync(s.TaskId, b, WorkContextFixture.Principal(s.B), default)).Operations);
        }
        var page = (await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments"))!;
        Assert.Equal(100, page.History.Select(x => x.DraftVersion).Distinct().Count()); Assert.Equal(101, page.DraftVersion);
        Assert.Equal(previous, Assert.Single(page.Associations).LastOperation); Assert.True(page.Associations[0].IsAttached);
        Assert.Equal(2, page.HistoryBeforeDraftVersion);
        var older = (await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments?beforeDraftVersion=" + page.HistoryBeforeDraftVersion))!;
        Assert.Equal(1, Assert.Single(older.History).DraftVersion); Assert.Null(older.HistoryBeforeDraftVersion);
        Assert.Equal(previous, Assert.Single(older.Associations).LastOperation); Assert.Equal(101, await p.F.Read(db => db.DraftAttachmentOperations.CountAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().GetAsync(s.Root + "/shared-draft/attachments?beforeDraftVersion=0")).StatusCode);
    }
    [Fact] public async Task Detach_missing_Removed_photo_preserves_accepted_history() { await using var p = await PhotoFixture.Create(); await UnavailableDetach(p); }
    internal static async Task UnavailableDetach(PhotoFixture p) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var r = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a))).Operations);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1)); File.Delete(await p.FinalPath(a.AttachmentId));
        var state = (await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments"))!;
        Assert.False(Assert.Single(state.Associations).IsCurrentApplicable); Assert.Equal("MissingUnexpected", state.Associations[0].Availability);
        await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, 2, previous: r, kind: "DetachAttachment"));
        Assert.Equal(2, await p.F.Read(db => db.DraftAttachmentOperations.CountAsync())); Assert.Equal(1, await p.F.Read(db => db.AttachmentAcceptances.CountAsync()));
        Assert.False(await p.F.Read(db => db.DraftAttachmentProjections.Select(x => x.IsAttached).SingleAsync()));
        Assert.Equal(r, await s.Client().GetFromJsonAsync<DraftAttachmentReceipt>(s.Root + $"/operations/{r.OperationId}/receipt"));
        var again = DraftAttachmentTests.Input(s, a, 3, previous: Assert.Single((await s.Client().GetFromJsonAsync<DraftAttachmentHistory>(s.Root + "/shared-draft/attachments"))!.History, x => x.Kind == "DetachAttachment"));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), again)).StatusCode);
    }
}
