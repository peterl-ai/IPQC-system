using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasureCanonicalTests
{
    [Fact] public void Frozen_V1_bytes_exclude_transport_and_preserve_exact_text() {
        Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("D12"));
        var b = new UnableToMeasureBatchInput(Id(9), 1, Id(2), 2, "synthetic", 3,
            new(Id(3), 1, "SetUnableToMeasureNG", Id(4), Id(5), Id(6), Id(7), 3, 1, "Probe blocked", new(Id(8), new string('a', 64)), null), new(Id(6), "not-in-business-hash"));
        const string expected = """{"domain":"unable-to-measure","v":1,"task":"00000000-0000-0000-0000-000000000001","environment":"synthetic","line":"00000000-0000-0000-0000-000000000002","workflow":2,"operation":"00000000-0000-0000-0000-000000000003","kind":"SetUnableToMeasureNG","item":"00000000-0000-0000-0000-000000000004","attempt":"00000000-0000-0000-0000-000000000005","source":"00000000-0000-0000-0000-000000000006","contributorAssertion":"00000000-0000-0000-0000-000000000007","base":3,"local":1,"ngReason":"Probe blocked","evidence":{"operation":"00000000-0000-0000-0000-000000000008","hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"predecessor":null}""";
        Assert.Equal(expected, UnableToMeasureCanonical.Operation(Id(1), b)); var hash = DraftCanonical.Hash(expected);
        Assert.Equal(hash, DraftCanonical.Hash(UnableToMeasureCanonical.Operation(Id(1), b with { SourceProof = null, BatchId = Guid.NewGuid(), ExpectedDraftVersion = 99 })));
        Assert.Equal(UnableToMeasureCanonical.Batch(Id(1), b, hash), UnableToMeasureCanonical.Batch(Id(1), b with { SourceProof = null }, hash));
        Assert.NotEqual(expected, UnableToMeasureCanonical.Operation(Id(1), b with { Operation = b.Operation with { NgReason = "Probe blocked " } }));
        Assert.NotEqual(UnableToMeasureCanonical.Operation(Id(1), b with { Operation = b.Operation with { NgReason = "é" } }), UnableToMeasureCanonical.Operation(Id(1), b with { Operation = b.Operation with { NgReason = "e\u0301" } }));
        Assert.DoesNotContain("not-in-business-hash", expected);
    }
}
