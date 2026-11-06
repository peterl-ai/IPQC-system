using JaxPower.Ipqc.Api.Domain;

namespace JaxPower.Ipqc.Api.Services;

public sealed class UnsupportedWorkflowException() : Exception("This workflow is not supported by the legacy API.");

public static class PatrolWorkflowPolicy
{
    public static bool IsLegacy(WorkflowVersion version) => version == WorkflowVersion.LegacyPersonalV1;

    public static void RequireLegacy(WorkflowVersion version)
    {
        if (!IsLegacy(version)) throw new UnsupportedWorkflowException();
    }
}
