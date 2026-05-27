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
    /// Creates a new <see cref="MSBuildWorkspace"/>, attaches the diagnostics sink, and opens
    /// the supplied solution or project path. The caller owns disposal of the returned
    /// workspace on both success (transferred to the session) and failure (the manager's
    /// finally block disposes the partial workspace if this method throws after returning a
    /// reference, but the throw-before-return case is the loader's responsibility).
    /// </summary>
    public virtual async Task<MSBuildWorkspace> CreateAndOpenAsync(
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

            var isolatedAnalyzerReferences = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                workspace.CurrentSolution,
                workspaceId,
                logger);
            if (isolatedAnalyzerReferences > 0)
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: retargeted {Count} analyzer reference(s) to shadow-copy loaders.",
                    workspaceId,
                    isolatedAnalyzerReferences);
            }

            return workspace;
        }
        catch
        {
            // Partial-load failure: dispose the half-initialized workspace before propagating
            // so the caller does not have to track ownership of a return-value-not-yet-handed-out.
            workspace.Dispose();
            throw;
        }
    }
}
