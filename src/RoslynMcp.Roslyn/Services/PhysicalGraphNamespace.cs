using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Admits dedicated service namespaces only after source-observation disjointness.</summary>
internal static class PhysicalGraphNamespace
{
    internal static string StagingParent => Path.Combine(Path.GetTempPath(), "roslynmcp-staging");
    internal static string ForkParent => Path.Combine(Path.GetTempPath(), "roslynmcp-forks");

    internal static async Task<PhysicalDirectoryScope> OpenParentAsync(string workspaceId, Solution source,
        PhysicalGraphLayout layout, string parent, string child, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        if ((Path.GetFileName(parent) is not ("roslynmcp-staging" or "roslynmcp-forks"))
            || !FileSystemPath.Comparer.Equals(Path.GetDirectoryName(child), parent))
            throw InvalidOperationErrors.Internal("A physical graph namespace escaped its dedicated service parent.");
        PhysicalDirectoryScope? existing = null;
        PhysicalDirectoryScope? issued = null;
        Exception? primary = null;
        try
        {
            // Inspect the existing service route without following aliases, before the
            // physical disjointness resolver can observe that route through a link.
            existing = PhysicalDirectoryScope.OpenRouteIfPresent(parent);
            existing?.AssertCurrentPath();
            var anchorPath = Path.GetDirectoryName(parent)
                ?? throw InvalidOperationErrors.Internal("A dedicated service namespace has no physical parent.");
            issued = await PhysicalGraphDirectory.OpenParentAsync(anchorPath, [Path.GetFileName(parent)],
                Path.GetFileName(child), AssertAdmissionAsync, ct).ConfigureAwait(false);
            var releasing = existing;
            existing = null;
            releasing?.Dispose();
            var result = issued;
            issued = null;
            return result;

            async Task AssertAdmissionAsync(IReadOnlyList<string> inventory, CancellationToken ct)
            {
                var certificate = NativeCompilerContextCertificate.Require(source);
                var roots = layout.BuildNodes.SelectMany(node => node.PathEvidence.TraversalRoots).Distinct(FileSystemPath.Comparer).ToArray();
                await certificate.AssertTemporaryPathDisjointAsync(source, workspaceId, policy, parent, roots, ct).ConfigureAwait(false);
                await certificate.AssertTemporaryPathDisjointAsync(source, workspaceId, policy, child, roots, ct).ConfigureAwait(false);
                existing?.AssertCurrentPath();
                foreach (var probe in layout.BuildNodes.SelectMany(node => node.PathEvidence.ConditionProbes))
                {
                    ct.ThrowIfCancellationRequested();
                    var changedParent = existing is null && FileSystemPath.Comparer.Equals(probe.Path, parent);
                    var changedChild = FileSystemPath.Comparer.Equals(probe.Path, child) || FileSystemPath.IsStrictDescendant(child, probe.Path);
                    if (changedParent || changedChild)
                        throw new PublicInvalidOperationException("A private graph namespace would change an observed native source condition.");
                }
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Service graph parent admission release failed.", primary, issued, existing); }
    }
}
