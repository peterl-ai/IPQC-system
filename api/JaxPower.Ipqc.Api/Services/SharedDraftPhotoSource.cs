using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    // Must run inside WorkContextStore with this source ID included in account-first locks.
    internal async Task ValidatePhotoSource(PatrolTask task, Guid clientPhotoId, Guid contextId, Guid ownerId,
        string proof, PersonalAuthority authority, CancellationToken ct)
    {
        try {
            _ = await Sources(task, [new DraftSourceProof(contextId, proof)],
                [new SourceClaim(clientPhotoId, contextId, ownerId, [])], authority, ct);
        } catch (DraftQuarantineException ex) {
            // A photo reservation is not a draft operation. Do not expose its client ID
            // as an affected operation or imply that any business operation was accepted.
            throw new PersonalAccessException(ex.Message,409);
        }
    }
}
