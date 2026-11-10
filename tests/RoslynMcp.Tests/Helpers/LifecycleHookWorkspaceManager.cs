using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Pass-through over a REAL workspace manager (real close ordering, real lifecycle events) that lets a
/// test run a deterministic hook at the exact point a service has just read workspace state. A hook
/// typically closes or reloads the workspace through the real manager, so the service resumes in the
/// window between its version/status read and its cache publish. Hooks never sleep: gate with
/// <see cref="TaskCompletionSource"/> / <see cref="ManualResetEventSlim"/>.
/// </summary>
internal sealed class LifecycleHookWorkspaceManager(IWorkspaceManager inner) : DelegatingWorkspaceManager(inner)
{
    /// <summary>Runs synchronously after <see cref="GetCurrentVersion"/> returned its value.</summary>
    public Action<string>? AfterGetCurrentVersion { get; set; }

    /// <summary>Awaited after <see cref="GetStatusAsync"/> captured its status, before it is returned.</summary>
    public Func<string, Task>? AfterGetStatusAsync { get; set; }

    public override int GetCurrentVersion(string workspaceId)
    {
        var version = base.GetCurrentVersion(workspaceId);
        AfterGetCurrentVersion?.Invoke(workspaceId);
        return version;
    }

    public override async Task<WorkspaceStatusDto> GetStatusAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        var status = await base.GetStatusAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (AfterGetStatusAsync is { } hook)
        {
            await hook(workspaceId).ConfigureAwait(false);
        }

        return status;
    }
}
