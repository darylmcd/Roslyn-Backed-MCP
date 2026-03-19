namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents NuGet package usage information for a workspace or project set.
/// </summary>
public sealed record NuGetDependencyResultDto(
    IReadOnlyList<NuGetPackageDto> Packages,
    IReadOnlyList<NuGetProjectDto> Projects);

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
    string Version);
