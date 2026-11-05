using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Write-set admissions for tests that drive <see cref="ICompositeApplyOrchestrator"/> directly.
/// </summary>
internal static class CompositeAdmission
{
    /// <summary>
    /// Admits every write set. For tests about orchestrator behaviour that is not the host's
    /// sanctioned-root boundary (the boundary itself is covered through <c>OrchestrationTools</c>).
    /// </summary>
    public static readonly CompositeWriteSetAdmission AdmitAll = static (_, _, _) => Task.CompletedTask;
}
