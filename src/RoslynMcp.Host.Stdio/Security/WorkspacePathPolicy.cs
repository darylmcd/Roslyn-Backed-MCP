using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>Applies configured and request-scoped roots to complete isolation and recovery inventories.</summary>
internal sealed class WorkspacePathPolicy(SecurityOptions securityOptions) : IWorkspacePathPolicy
{
    public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var expand = RootExpansionGrantRegistry.IsGranted(workspaceId);
        foreach (var path in paths.Distinct(FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            var canonical = await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                RequestMcpServerContext.Current, path, ct, securityOptions: securityOptions,
                expandSanctionedRoots: expand).ConfigureAwait(false);
            if (!FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                    Path.TrimEndingDirectorySeparator(canonical)))
                throw WorkspacePathPolicyRefusal.Mark(new PublicArgumentException(
                    "Workspace isolation and recovery require physical canonical paths. Reload the workspace through its physical path.",
                    nameof(paths)), WorkspacePathPolicyRefusalKind.AliasedPath);
        }
    }
}
