using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftEvidenceAssessmentPostgresTests
{
    private static async Task Run(Func<PhotoFixture, Task> test) { await using var p = await PhotoFixture.Create(true); await test(p); }
    [DisposablePostgresFact] public Task Genuine() => Run(DraftEvidenceAssessmentTests.Genuine);
    [DisposablePostgresFact] public Task NoPhoto() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "none"));
    [DisposablePostgresFact] public Task UnassociatedReady() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "ready-unassociated"));
    [DisposablePostgresFact] public Task Pending() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "pending"));
    [DisposablePostgresFact] public Task Detached() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "detached"));
    [DisposablePostgresFact] public Task Missing() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "missing"));
    [DisposablePostgresFact] public Task Corrupt() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "corrupt"));
    [DisposablePostgresFact] public Task Unavailable() => Run(p => DraftEvidenceAssessmentTests.Availability(p, "unavailable"));
    [DisposablePostgresFact] public Task RawRequired() => Run(p => DraftEvidenceAssessmentTests.Policy(p, "Required"));
    [DisposablePostgresFact] public Task RawOptional() => Run(p => DraftEvidenceAssessmentTests.Policy(p, "Optional"));
    [DisposablePostgresFact] public Task RawLowercase() => Run(p => DraftEvidenceAssessmentTests.Policy(p, "optional"));
    [DisposablePostgresFact] public Task RawEmpty() => Run(p => DraftEvidenceAssessmentTests.Policy(p, ""));
    [DisposablePostgresFact] public Task RawUnknown() => Run(p => DraftEvidenceAssessmentTests.Policy(p, "Legacy photo needed"));
    [DisposablePostgresFact] public Task NgToOk() => Run(p => DraftEvidenceAssessmentTests.Transition(p, "NG", "OK"));
    [DisposablePostgresFact] public Task NgToNg() => Run(p => DraftEvidenceAssessmentTests.Transition(p, "NG", "NG"));
    [DisposablePostgresFact] public Task OkToNg() => Run(p => DraftEvidenceAssessmentTests.Transition(p, "OK", "NG"));
    [DisposablePostgresFact] public Task SameBatch() => Run(DraftEvidenceAssessmentTests.WithinBatch);
    [DisposablePostgresFact] public Task RemovedNgSample() => Run(DraftEvidenceAssessmentTests.RemovedSample);
    [DisposablePostgresFact] public Task Qualitative() => Run(p => DraftEvidenceAssessmentTests.Multiple(p, false));
    [DisposablePostgresFact] public Task Quantitative() => Run(p => DraftEvidenceAssessmentTests.Multiple(p, true));
    [DisposablePostgresFact] public Task OtherItemAttempt() => Run(p => DraftEvidenceAssessmentTests.Scope(p, "item"));
    [DisposablePostgresFact] public Task OtherTask() => Run(p => DraftEvidenceAssessmentTests.Scope(p, "task"));
    [DisposablePostgresFact] public Task StaleAttempt() => Run(p => DraftEvidenceAssessmentTests.Scope(p, "stale"));
    [DisposablePostgresFact] public Task Dispositions() => Run(DraftEvidenceAssessmentTests.Dispositions);
    [DisposablePostgresFact] public Task SourceLogout() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "logout"));
    [DisposablePostgresFact] public Task SourceReset() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "reset"));
    [DisposablePostgresFact] public Task SourceRole() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "role"));
    [DisposablePostgresFact] public Task SourceDeactivate() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "deactivate"));
    [DisposablePostgresFact] public Task SourceExplicit() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "explicit"));
    [DisposablePostgresFact] public Task SourcePassword() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "self-password"));
    [DisposablePostgresFact] public Task SourceRefresh() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "refresh"));
    [DisposablePostgresFact] public Task SourceExpiry() => Run(p => DraftEvidenceAssessmentTests.SourceLifecycle(p, "expiry"));
    [DisposablePostgresFact] public Task Bounds() => Run(DraftEvidenceAssessmentTests.Bounds);
    [DisposablePostgresFact] public Task HistoryLimit() => Run(DraftEvidenceAssessmentTests.LongHistory);
    [DisposablePostgresFact] public Task Authority() => Run(DraftEvidenceAssessmentTests.Authority);
    [DisposablePostgresFact] public Task ReadBeforeDetach() => Run(p => DraftEvidenceAssessmentConcurrencyTests.Reference(p, true));
    [DisposablePostgresFact] public Task DetachBeforeRead() => Run(p => DraftEvidenceAssessmentConcurrencyTests.Reference(p, false));
    [DisposablePostgresFact] public Task LogoutBeforeRead() => Run(p => DraftEvidenceAssessmentConcurrencyTests.Logout(p, true));
    [DisposablePostgresFact] public Task ReadBeforeLogout() => Run(p => DraftEvidenceAssessmentConcurrencyTests.Logout(p, false));
}
