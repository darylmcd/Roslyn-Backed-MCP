using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Creates a fresh <see cref="MSBuildWorkspace"/> with optional MSBuild global-property
/// overrides, attaches a <see cref="WorkspaceDiagnosticsSink"/> for workspace-failed events,
/// opens the requested solution or project, and retargets file-based analyzer references onto
/// the shadow-copy loader. Extracted from <see cref="WorkspaceManager.LoadIntoSessionAsync"/>
/// so the per-load workspace-construction concern is independently testable and the
/// manager retains the atomic swap-on-success pattern that
/// <c>autoreload-cascade-stdio-host-crash</c> depends on (readers always observe either the
/// prior loaded workspace OR the fully initialized new workspace, never a half-initialized
/// session).
/// </summary>
/// <remarks>
/// Not sealed so <c>WorkspaceSessionLoaderFailureTests</c> can derive a test double that
/// simulates a partial-load failure without touching disk. <c>InternalsVisibleTo</c> on
/// <c>RoslynMcp.Roslyn</c> exposes the type to the test assembly.
/// </remarks>
internal class WorkspaceSessionLoader
{
    /// <summary>
    /// Creates a new <see cref="MSBuildWorkspace"/>, attaches the diagnostics sink, opens
    /// the supplied solution or project path, and retargets file-based analyzer references
    /// onto per-lease shadow-copy loaders. The caller owns disposal of the returned workspace
    /// AND analyzer-shadow lease on both success (transferred to the session) and failure
    /// (the manager's finally block disposes the partial workspace/lease if this method throws
    /// after returning, but the throw-before-return case is the loader's responsibility). The
    /// lease is never null: platforms/configurations where isolation cannot run get a no-op
    /// empty lease (analyzer-shadow-loader-lifecycle).
    /// </summary>
    public virtual async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
        string workspaceId,
        string path,
        IDictionary<string, string>? globalProperties,
        WorkspaceDiagnosticsSink diagnostics,
        ILogger logger,
        CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        // selfhosted-shadow-copy-analyzer-reference-test-fails: when caller passes MSBuild
        // global properties (e.g. Configuration=Release), thread them into MSBuildWorkspace
        // so ProjectReference OutputItemType="Analyzer" entries resolve against the right
        // bin/<config>/ tree. Without this, the default Configuration=Debug evaluation drops
        // analyzer references on Release-only checkouts (CI runners) because the Debug
        // TargetPath does not exist on disk.
        var workspace = (globalProperties is { Count: > 0 })
            ? MSBuildWorkspace.Create(globalProperties)
            : MSBuildWorkspace.Create();
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? analyzerLease = null;
        try
        {
            if (globalProperties is { Count: > 0 })
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: created MSBuildWorkspace with {Count} global property override(s): {Properties}",
                    workspaceId,
                    globalProperties.Count,
                    string.Join(", ", globalProperties.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            diagnostics.AttachTo(workspace, workspaceId, logger);

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                await workspace.OpenSolutionAsync(path, cancellationToken: ct).ConfigureAwait(false);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException($"Path must end with .sln, .slnx, or .csproj: {path}");
            }

            analyzerLease = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                workspace.CurrentSolution,
                workspaceId,
                logger);
            if (analyzerLease.RetargetedReferenceCount > 0)
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: retargeted {Count} analyzer reference(s) to shadow-copy loaders.",
                    workspaceId,
                    analyzerLease.RetargetedReferenceCount);
            }

            return (workspace, analyzerLease);
        }
        catch
        {
            // Partial-load failure: dispose the half-initialized workspace (and any analyzer
            // shadow lease created before the throw — lease after workspace, so Roslyn has
            // released the analyzer references first) before propagating, so the caller does
            // not have to track ownership of a return-value-not-yet-handed-out.
            workspace.Dispose();
            analyzerLease?.Dispose();
            throw;
        }
    }
}
