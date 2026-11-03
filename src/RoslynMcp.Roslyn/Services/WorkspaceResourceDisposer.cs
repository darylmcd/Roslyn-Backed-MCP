using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal class WorkspaceResourceDisposer
{
    internal static WorkspaceResourceDisposer Default { get; } = new();

    public virtual void DisposeWorkspace(MSBuildWorkspace? workspace) => workspace?.Dispose();
    public virtual void DisposeLease(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease) => lease?.Dispose();
    public virtual void DisposeLoadLock(SemaphoreSlim loadLock) => loadLock.Dispose();

    internal static List<Exception> DisposeOrdered(Action disposeWorkspace, Action disposeLease, Action? disposeLoadLock = null)
    {
        var failures = new List<Exception>();
        Attempt(disposeWorkspace, failures);
        Attempt(disposeLease, failures);
        if (disposeLoadLock is not null) Attempt(disposeLoadLock, failures);
        return failures;
    }

    private static void Attempt(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }
}
