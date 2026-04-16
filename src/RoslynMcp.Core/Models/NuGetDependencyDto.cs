namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents NuGet package usage information for a workspace or project set.
/// </summary>
/// <param name="Summaries">
/// Compact per-package summary populated when <c>get_nuget_dependencies</c> is invoked
/// with <c>summary=true</c>. Drops per-project / per-version detail (see
/// <see cref="Packages"/>) so the response stays under the MCP cap on large solutions —
/// Jellyfin's 40-project graph dropped from ~102 KB to single-digit KB. Mutually
/// exclusive with <see cref="Packages"/> + <see cref="Projects"/>: when
/// <c>summary=true</c>, only <see cref="Summaries"/> is populated; when
/// <c>summary=false</c> (default), only the verbose lists are populated.
/// </param>
public sealed record NuGetDependencyResultDto(
    IReadOnlyList<NuGetPackageDto> Packages,
    IReadOnlyList<NuGetProjectDto> Projects,
    IReadOnlyList<NuGetPackageSummaryDto>? Summaries = null);

/// <summary>
/// Compact per-package summary used by <c>get_nuget_dependencies summary=true</c>.
/// </summary>
/// <param name="PackageId">Package id (unique across the workspace).</param>
/// <param name="Version">
/// "Winning" version when the package is referenced once. When multiple versions are
/// observed (i.e. <see cref="DistinctVersionCount"/> &gt; 1), this contains the first
/// version encountered alphabetically — callers wanting full detail should re-run with
/// <c>summary=false</c>.
/// </param>
/// <param name="ProjectCount">Number of projects that reference this package.</param>
/// <param name="DistinctVersionCount">
/// Number of distinct versions referenced for this package across the workspace. A value
/// greater than 1 indicates a version-drift hazard worth investigating.
/// </param>
public sealed record NuGetPackageSummaryDto(
    string PackageId,
    string Version,
    int ProjectCount,
    int DistinctVersionCount);

/// <summary>
/// Represents a NuGet package and the projects that use it.
/// </summary>
public sealed record NuGetPackageDto(
    string PackageId,
    string Version,
    IReadOnlyList<string> UsedByProjects);

/// <summary>
/// Represents a project and its referenced NuGet packages.
/// </summary>
public sealed record NuGetProjectDto(
    string ProjectName,
    string ProjectPath,
    IReadOnlyList<NuGetPackageReferenceDto> PackageReferences);

/// <summary>
/// Represents a single NuGet package reference declared by a project.
/// </summary>
public sealed record NuGetPackageReferenceDto(
    string PackageId,
    string Version,
    string? ResolvedCentralVersion = null);
