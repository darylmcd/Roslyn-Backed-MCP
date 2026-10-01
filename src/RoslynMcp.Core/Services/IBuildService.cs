using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Builds a workspace or individual project and returns structured compiler diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// A build is three steps so callers can hold the workspace gate only for the steps that touch
/// workspace state and run the long <c>dotnet build</c> command outside it:
/// <see cref="PrepareWorkspaceBuildAsync"/> / <see cref="PrepareProjectBuild"/> (gated read),
/// <see cref="RunBuildCommandAsync"/> (no gate; bounded by the build timeout), and
/// <see cref="CompleteBuildAsync"/> (gated read). <see cref="BuildWorkspaceAsync"/> and
/// <see cref="BuildProjectAsync"/> compose the three steps without any gate.
/// </para>
/// </remarks>
public interface IBuildService
{
    Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct);
    Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct);

    /// <summary>Resolves the loaded solution/project target and records the workspace version.</summary>
    Task<BuildCommandPlan> PrepareWorkspaceBuildAsync(string workspaceId, CancellationToken ct);

    /// <summary>Resolves the named project target and records the workspace version.</summary>
    BuildCommandPlan PrepareProjectBuild(string workspaceId, string projectName);

    /// <summary>
    /// Runs the planned <c>dotnet build</c>. Call outside the workspace gate: the command's own
    /// gates serialize it and its timeout covers queue wait plus execution.
    /// </summary>
    Task<BuildCommandRun> RunBuildCommandAsync(BuildCommandPlan plan, CancellationToken ct);

    /// <summary>
    /// Parses diagnostics and enriches their spans against the current solution when the
    /// workspace version still matches the plan; otherwise skips enrichment and adds the
    /// <c>workspaceChangedDuringRun</c> warning.
    /// </summary>
    Task<BuildResultDto> CompleteBuildAsync(BuildCommandRun run, CancellationToken ct);

    /// <summary>
    /// Builds the result without touching workspace state (workspace closed while the command
    /// ran): no span enrichment, with the <c>workspaceChangedDuringRun</c> warning.
    /// </summary>
    BuildResultDto CompleteBuildWithoutWorkspace(BuildCommandRun run);
}

/// <summary>A resolved build target plus the workspace version it was resolved against.</summary>
public sealed record BuildCommandPlan(
    string WorkspaceId,
    string TargetPath,
    IReadOnlyList<string> Arguments,
    int WorkspaceVersion);

/// <summary>A finished build command and how long it took (queue wait included).</summary>
public sealed record BuildCommandRun(
    BuildCommandPlan Plan,
    CommandExecutionDto Execution,
    long CommandDurationMs);
