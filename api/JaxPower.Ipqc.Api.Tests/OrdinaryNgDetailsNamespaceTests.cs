using System.Net;
using System.Net.Http.Json;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.OrdinaryNgDetailsFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OrdinaryNgDetailsNamespaceTests
{
    [Fact]
    public async Task Existing_operation_domains_cannot_share_an_ordinary_metadata_identity_in_either_direction()
    {
        await using var p = await PhotoFixture.Create();
        await Namespace(p);
    }

    internal static async Task Namespace(PhotoFixture p)
    {
        var fixture = await NgPicSelectionFixture.Setup(p.F);
        var s = fixture.S;
        var first = (await Accept(s, Input(s))).Operation;
        var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var binding = new BindingSetup(s, fixture.Sample!, photo, reference!);
        var disposition = new UnableSetup(s, photo, reference!);
        var sample = s.Batch(s.Capture(version: 3), 3);
        var remove = ItemRemovalTests.Input(s, 3);
        var attach = DraftAttachmentTests.Input(s, photo, 3, previous: reference, kind: "DetachAttachment");
        var unable = UnableToMeasureTests.Input(disposition, 3);
        var evidence = SampleEvidenceTests.Input(binding, 3);
        var header = BasicInformationFixture.Input(s, 3);
        var pic = NgPicSelectionFixture.Input(fixture, 3);
        var id = first.OperationId;
        using var client = s.Client();
        var before = await Facts(p.F);
        async Task Collision<T>(string url, T body)
        {
            using var response = await client.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
        }
        await Collision(s.Root + "/operation-batches", sample with { Operations = [sample.Operations[0] with { OperationId = id }] });
        await Collision(ItemRemovalTests.Url(s), remove with { Operation = remove.Operation with { OperationId = id } });
        await Collision(DraftAttachmentTests.Url(s), attach with { Operations = [attach.Operations[0] with { OperationId = id }] });
        await Collision(UnableToMeasureTests.Url(s), unable with { Operation = unable.Operation with { OperationId = id } });
        await Collision(SampleEvidenceTests.Url(s), evidence with { Operation = evidence.Operation with { OperationId = id, BindingId = id } });
        await Collision(BasicInformationFixture.WriteUrl(s), header with { Operation = header.Operation with { OperationId = id } });
        await Collision(NgPicSelectionFixture.Url(s), pic with { Operation = pic.Operation with { OperationId = id } });
        Assert.Equal(before, await Facts(p.F));

        var h = (await BasicInformationFixture.Accept(s, header)).Operation;
        var bind = (await SampleEvidenceTests.Accept(binding, SampleEvidenceTests.Input(binding, 4))).Operation;
        var selected = (await NgPicSelectionFixture.Accept(fixture, NgPicSelectionFixture.Input(fixture, 5))).Operation;
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        var removed = (await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 6, other))).Operation;
        var un = (await UnableToMeasureTests.Accept(disposition, UnableToMeasureTests.Input(disposition, 7))).Operation;
        before = await Facts(p.F);
        foreach (var existing in new[] { fixture.Sample!.OperationId, reference!.OperationId, h.OperationId, bind.OperationId,
            selected.OperationId, removed.OperationId, un.OperationId })
        {
            var input = Input(s, 8, first);
            await Reject(s, input with { Operation = input.Operation with { OperationId = existing } },
                HttpStatusCode.Conflict, "OperationIdentityConflict");
        }
        Assert.Equal(before, await Facts(p.F));
    }
}
