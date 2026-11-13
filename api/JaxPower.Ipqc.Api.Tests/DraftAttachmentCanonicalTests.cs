using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftAttachmentCanonicalTests
{
    [Fact] public void Frozen_V1_vector_and_transport_exclusion() {
        Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("D12"));
        var o = new DraftAttachmentOperationInput(Id(3), 1, "AssociateAttachment", Id(4), Id(5), Id(6), Id(7), new string('a', 64), Id(8), Id(9), 0, 1, null);
        var b = new DraftAttachmentBatchInput(Id(10), 1, Id(2), 2, "synthetic", 0, [o], [new(Id(8), "secret")]);
        const string expected = """{"domain":"draft-attachment","v":1,"environment":"synthetic","task":"00000000-0000-0000-0000-000000000001","line":"00000000-0000-0000-0000-000000000002","workflow":2,"operation":"00000000-0000-0000-0000-000000000003","kind":"AssociateAttachment","item":"00000000-0000-0000-0000-000000000004","attempt":"00000000-0000-0000-0000-000000000005","attachment":"00000000-0000-0000-0000-000000000006","clientPhoto":"00000000-0000-0000-0000-000000000007","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","source":"00000000-0000-0000-0000-000000000008","contributorAssertion":"00000000-0000-0000-0000-000000000009","base":0,"local":1,"predecessor":null}""";
        Assert.Equal(expected, DraftAttachmentCanonical.Operation(Id(1), b, o)); Assert.DoesNotContain("secret", expected);
        var h = new Dictionary<Guid, string> { [Id(3)] = DraftCanonical.Hash(expected), [Id(12)] = new string('B', 64) };
        Assert.Equal(DraftAttachmentCanonical.Batch(Id(1), b, h), DraftAttachmentCanonical.Batch(Id(1), b with { SourceProofs = [] }, h.Reverse().ToDictionary()));
        Assert.Equal(expected, DraftAttachmentCanonical.Operation(Id(1), b, o with { Sha256 = o.Sha256.ToUpperInvariant() }));
        Assert.NotEqual(expected, DraftAttachmentCanonical.Operation(Id(1), b, o with { SourceWorkContextId = Id(11) }));
        Assert.NotEqual(expected, DraftAttachmentCanonical.Operation(Id(1), b, o with { Predecessor = new(Id(11), new string('B', 64)) }));
    }
}
