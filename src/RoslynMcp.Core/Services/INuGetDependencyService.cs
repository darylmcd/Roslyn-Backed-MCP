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

    /// <summary>
    /// Scans NuGet package references for known vulnerabilities using
    /// <c>dotnet list package --vulnerable</c>.
    /// </summary>
    Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct);
}
