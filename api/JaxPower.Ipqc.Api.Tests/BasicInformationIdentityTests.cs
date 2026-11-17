using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.BasicInformationFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationIdentityTests
{
    [Fact] public void Fixed_protocol_vectors_do_not_depend_on_transport_proof_or_normalize_Unicode() {
        Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("D12"));
        var b = new BasicInformationBatchInput(Id(6), 1, Id(2), 2, "synthetic-header-vector", 7,
            new(Id(3), 1, "CaptureBasicInformationField", BasicInformationField.Customer, Id(4), Id(5), 7, 9, "é", null), new(Id(4), "synthetic-transport-proof"));
        const string expected = """{"domain":"basic-information-field","v":1,"environment":"synthetic-header-vector","task":"00000000-0000-0000-0000-000000000001","line":"00000000-0000-0000-0000-000000000002","workflow":2,"operation":"00000000-0000-0000-0000-000000000003","kind":"CaptureBasicInformationField","field":"Customer","value":"\u00E9","source":"00000000-0000-0000-0000-000000000004","contributorAssertion":"00000000-0000-0000-0000-000000000005","base":7,"local":9,"predecessor":null}""";
        var payload = BasicInformationCanonical.Operation(Id(1), b); Assert.Equal(expected, payload);
        const string hash = "0822F12248A4D7059744C1E43360B04189EB2EBEDD3E2ED733CC6F0D9596B2E4";
        Assert.Equal(hash, DraftCanonical.Hash(payload)); Assert.Equal("00C7A10F825F83E18B01E776AAB8C9D9CF08EB7164CD92FF6502A532B6CDBEE0", BasicInformationCanonical.Batch(Id(1), b, hash));
        Assert.Equal(payload, BasicInformationCanonical.Operation(Id(1), b with { SourceProof = null }));
        Assert.NotEqual(payload, BasicInformationCanonical.Operation(Id(1), b with { Operation = b.Operation with { Value = "e\u0301" } }));
        var hashes = new[] { null, "", " " }.Select(value => DraftCanonical.Hash(BasicInformationCanonical.Operation(Id(1), b with { Operation = b.Operation with { Value = value } }))).ToArray();
        Assert.Equal(3, hashes.Distinct().Count());
    }
    [Fact] public void Predecessor_hex_case_is_canonical_but_scope_source_and_origin_are_not_interchangeable() {
        Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("D12"));
        var b = new BasicInformationBatchInput(Id(6), 1, Id(2), 2, "synthetic", 7,
            new(Id(3), 1, "CorrectBasicInformationField", BasicInformationField.Customer, Id(4), Id(5), 7, 9, null, new(Id(8), new string('a',64))), null);
        var payload = BasicInformationCanonical.Operation(Id(1), b);
        Assert.Equal(payload, BasicInformationCanonical.Operation(Id(1), b with { Operation = b.Operation with { Predecessor = new(Id(8), new string('A',64)) } }));
        foreach (var changed in new[] { b with { ExpectedTaskLineId = Id(9) }, b with { ExpectedEnvironmentId = "other" }, b with { ExpectedWorkflowVersion = 1 },
            b with { Operation = b.Operation with { Field = BasicInformationField.Production } }, b with { Operation = b.Operation with { SourceWorkContextId = Id(10) } },
            b with { Operation = b.Operation with { ContributorUserIdAssertion = Id(10) } }, b with { Operation = b.Operation with { OriginLocalEditVersion = 10 } },
            b with { Operation = b.Operation with { BaseDraftVersion = 8 } } }) Assert.NotEqual(payload, BasicInformationCanonical.Operation(Id(1), changed));
        Assert.NotEqual(payload, BasicInformationCanonical.Operation(Id(11), b));
    }
    [Fact] public async Task Every_accepted_domain_shares_the_same_native_operation_namespace() { await using var p = await PhotoFixture.Create(); await Namespace(p); }
    internal static async Task Namespace(PhotoFixture p) {
        var f = p.F; var s = await SharedDraftFixture.Setup(f); var header = (await Accept(s, Input(s))).Operation;
        var sample = await DraftEvidenceAssessmentTests.Sample(s); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s); var binding = new BindingSetup(s, sample, photo, reference!);
        var before = await Facts(f); var sampleInput = s.Batch(s.Capture(version: 3), 3); var removal = ItemRemovalTests.Input(s, 3);
        var attachment = DraftAttachmentTests.Input(s, photo, 3, previous: reference, kind: "DetachAttachment");
        var unable = UnableToMeasureTests.Input(new(s, photo, reference!), 3); var bind = SampleEvidenceTests.Input(binding, 3);
        var attempts = new[] {
            s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sampleInput with { Operations = [sampleInput.Operations[0] with { OperationId = header.OperationId }] }),
            s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), removal with { Operation = removal.Operation with { OperationId = header.OperationId } }),
            s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), attachment with { Operations = [attachment.Operations[0] with { OperationId = header.OperationId }] }),
            s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), unable with { Operation = unable.Operation with { OperationId = header.OperationId } }),
            s.Client().PostAsJsonAsync(SampleEvidenceTests.Url(s), bind with { Operation = bind.Operation with { OperationId = header.OperationId, BindingId = header.OperationId } }) };
        foreach (var response in await Task.WhenAll(attempts)) { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync()); }
        Assert.Equal(before, await Facts(f));
        var bound = (await SampleEvidenceTests.Accept(binding, SampleEvidenceTests.Input(binding, 3))).Operation;
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        var removed = (await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 4, other))).Operation;
        var unmeasurable = (await UnableToMeasureTests.Accept(new(s, photo, reference!), UnableToMeasureTests.Input(new(s, photo, reference!), 5))).Operation;
        before = await Facts(f);
        foreach (var id in new[] { sample.OperationId, reference!.OperationId, bound.OperationId, removed.OperationId, unmeasurable.OperationId }) {
            var b = Input(s, 6, BasicInformationField.Production); b = b with { Operation = b.Operation with { OperationId = id } };
            var response = await s.Client().PostAsJsonAsync(WriteUrl(s), b); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, await Facts(f)); Assert.Equal(1, await f.Read(db => db.TaskHeaderOperations.CountAsync())); Assert.Equal(6, await DraftEvidenceAssessmentTests.Version(s));
    }
}
