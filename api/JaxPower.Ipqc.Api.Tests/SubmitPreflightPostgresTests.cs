using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SubmitPreflightPostgresTests
{
    private static async Task Run(Func<PhotoFixture, Task> test) { await using var p = await PhotoFixture.Create(true); await test(p); }
    [DisposablePostgresFact] public Task OrdinaryQualitative() => Run(p => SubmitPreflightTests.Ordinary(p, false));
    [DisposablePostgresFact] public Task OrdinaryQuantitative() => Run(p => SubmitPreflightTests.Ordinary(p, true));
    [DisposablePostgresFact] public Task RequiredMissing() => Run(p => SubmitPreflightTests.Required(p, "none"));
    [DisposablePostgresFact] public Task UnassociatedReady() => Run(p => SubmitPreflightTests.Required(p, "ready"));
    [DisposablePostgresFact] public Task Pending() => Run(p => SubmitPreflightTests.Required(p, "pending"));
    [DisposablePostgresFact] public Task RequiredValid() => Run(p => SubmitPreflightTests.Required(p, "valid"));
    [DisposablePostgresFact] public Task Detached() => Run(p => SubmitPreflightTests.Required(p, "detached"));
    [DisposablePostgresFact] public Task MissingBytes() => Run(p => SubmitPreflightTests.Required(p, "missing"));
    [DisposablePostgresFact] public Task CorruptBytes() => Run(p => SubmitPreflightTests.Required(p, "corrupt"));
    [DisposablePostgresFact] public Task UnavailableBytes() => Run(p => SubmitPreflightTests.Required(p, "unavailable"));
    [DisposablePostgresFact] public Task Minimum() => Run(SubmitPreflightTests.Minimum);
    [DisposablePostgresFact] public Task NgToOk() => Run(p => SubmitPreflightTests.Transition(p, "OK"));
    [DisposablePostgresFact] public Task RepeatedNg() => Run(p => SubmitPreflightTests.Transition(p, "NG"));
    [DisposablePostgresFact] public Task Removed() => Run(p => SubmitPreflightTests.Removed(p, false));
    [DisposablePostgresFact] public Task AllRemoved() => Run(p => SubmitPreflightTests.Removed(p, true));
    [DisposablePostgresFact] public Task UnableValid() => Run(p => SubmitPreflightTests.Unable(p, false));
    [DisposablePostgresFact] public Task UnableInvalid() => Run(p => SubmitPreflightTests.Unable(p, true));
    [DisposablePostgresFact] public Task LegacyRequired() => Run(p => SubmitPreflightTests.Legacy(p, "Required"));
    [DisposablePostgresFact] public Task LegacyOptional() => Run(p => SubmitPreflightTests.Legacy(p, "Optional"));
    [DisposablePostgresFact] public Task LegacyUnknown() => Run(p => SubmitPreflightTests.Legacy(p, "Unrecognized text"));
    [DisposablePostgresFact] public Task InvalidSnapshot() => Run(p => SubmitPreflightTests.Unavailable(p, "snapshot"));
    [DisposablePostgresFact] public Task MissingAttempt() => Run(p => SubmitPreflightTests.Unavailable(p, "attempt"));
    [DisposablePostgresFact] public Task EmptyFrozenScope() => Run(p => SubmitPreflightTests.Unavailable(p, "empty"));
    [DisposablePostgresFact] public Task LongHistory() => Run(SubmitPreflightTests.History);
    [DisposablePostgresFact] public Task BindingLimit() => Run(SubmitPreflightTests.BindingLimit);
    [DisposablePostgresFact] public Task SourceLogout() => Run(p => SubmitPreflightTests.SourceLifecycle(p, "logout"));
    [DisposablePostgresFact] public Task SourcePassword() => Run(p => SubmitPreflightTests.SourceLifecycle(p, "self-password"));
    [DisposablePostgresFact] public Task SourceReset() => Run(p => SubmitPreflightTests.SourceLifecycle(p, "reset"));
    [DisposablePostgresFact] public Task Authority() => Run(SubmitPreflightSecurityTests.Authority);
    [DisposablePostgresFact] public Task Scope() => Run(SubmitPreflightSecurityTests.Scope);
    [DisposablePostgresFact] public Task Bounds() => Run(SubmitPreflightSecurityTests.Bounds);
    [DisposablePostgresFact] public Task ReadBeforeSample() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, true, "sample"));
    [DisposablePostgresFact] public Task SampleBeforeRead() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, false, "sample"));
    [DisposablePostgresFact] public Task ReadBeforeDetach() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, true, "detach"));
    [DisposablePostgresFact] public Task DetachBeforeRead() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, false, "detach"));
    [DisposablePostgresFact] public Task ReadBeforeRemoved() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, true, "removed"));
    [DisposablePostgresFact] public Task RemovedBeforeRead() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, false, "removed"));
    [DisposablePostgresFact] public Task ReadBeforeUnable() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, true, "unable"));
    [DisposablePostgresFact] public Task UnableBeforeRead() => Run(p => SubmitPreflightConcurrencyTests.Writer(p, false, "unable"));
    [DisposablePostgresFact] public Task PreflightBeforePolicyRead() => Run(p => SubmitPreflightConcurrencyTests.PolicyRead(p, true));
    [DisposablePostgresFact] public Task PolicyReadBeforePreflight() => Run(p => SubmitPreflightConcurrencyTests.PolicyRead(p, false));
    [DisposablePostgresFact] public Task ReadBeforeLogout() => Run(p => SubmitPreflightConcurrencyTests.Logout(p, false));
    [DisposablePostgresFact] public Task LogoutBeforeRead() => Run(p => SubmitPreflightConcurrencyTests.Logout(p, true));
}
