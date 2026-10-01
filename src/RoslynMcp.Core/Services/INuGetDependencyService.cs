using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Reports NuGet package dependencies and known vulnerabilities for a workspace.
/// </summary>
public interface INuGetDependencyService
{
    /// <summary>
    /// Returns the list of <c>PackageReference</c> items across the workspace, grouped by
    /// project and de-duplicated by (id, version). Reads evaluated MSBuild items so
    /// references inherited from <c>Directory.Build.props</c> match
    /// <c>evaluate_msbuild_items</c> and the real restore graph.
    /// </summary>
    /// <param name="summary">
    /// (get-nuget-dependencies-no-summary-mode) When <c>true</c>, populates only
    /// <see cref="NuGetDependencyResultDto.Summaries"/> with one
    /// <see cref="NuGetPackageSummaryDto"/> per package; the verbose
    /// <see cref="NuGetDependencyResultDto.Packages"/> + <see cref="NuGetDependencyResultDto.Projects"/>
    /// lists are emitted as empty arrays. Use this on multi-project solutions where the
    /// default response exceeds the MCP cap (Jellyfin's 40-project graph: ~102 KB).
    /// </param>
    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct, bool summary = false);

    async Task<NuGetDependencyScanResult> GetNuGetDependenciesDetailedAsync(
        string workspaceId, CancellationToken ct, bool summary = false)
    {
        var result = await GetNuGetDependenciesAsync(workspaceId, ct, summary).ConfigureAwait(false);
        return new NuGetDependencyScanResult(result, IsComplete: true, FailedProjectCount: 0);
    }

    /// <summary>
    /// Scans NuGet package references for known vulnerabilities using
    /// <c>dotnet list package --vulnerable</c>. Composes
    /// <see cref="PrepareVulnerabilityScanAsync"/> and <see cref="RunVulnerabilityScanAsync"/>
    /// without any workspace gate; the MCP tool runs the two steps separately so the gate is not
    /// held across the command.
    /// </summary>
    Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct);

    /// <summary>
    /// Resolves the scan target, workspace version and cache key (workspace state only), and
    /// returns the cached result when one exists. Call under the workspace read gate.
    /// </summary>
    Task<VulnerabilityScanPlan> PrepareVulnerabilityScanAsync(
        string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct);

    /// <summary>
    /// Runs the planned <c>dotnet list package --vulnerable</c>, parses it and caches the result
    /// only when the workspace version still equals the plan's. Call outside the workspace gate:
    /// the command's own gates serialize it and <c>VulnerabilityScanTimeout</c> covers queue wait
    /// plus execution.
    /// </summary>
    Task<NuGetVulnerabilityScanResultDto> RunVulnerabilityScanAsync(
        VulnerabilityScanPlan plan, CancellationToken ct);
}

/// <summary>
/// A resolved vulnerability-scan target plus the workspace version and cache key it was resolved
/// against. <see cref="Cached"/> is non-null on a cache hit, in which case no command needs to run.
/// </summary>
public sealed record VulnerabilityScanPlan(
    string WorkspaceId,
    string TargetPath,
    IReadOnlyList<string> Arguments,
    bool IncludeTransitive,
    string ProjectFilterKey,
    string LockfileHash,
    int WorkspaceVersion,
    NuGetVulnerabilityScanResultDto? Cached);
