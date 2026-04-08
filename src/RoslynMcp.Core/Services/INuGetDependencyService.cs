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
    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct);

    /// <summary>
    /// Scans NuGet package references for known vulnerabilities using
    /// <c>dotnet list package --vulnerable</c>.
    /// </summary>
    Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct);
}
