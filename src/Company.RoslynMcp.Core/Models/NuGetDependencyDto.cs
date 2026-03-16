namespace Company.RoslynMcp.Core.Models;

public sealed record NuGetDependencyResultDto(
    IReadOnlyList<NuGetPackageDto> Packages,
    IReadOnlyList<NuGetProjectDto> Projects);

public sealed record NuGetPackageDto(
    string PackageId,
    string Version,
    IReadOnlyList<string> UsedByProjects);

public sealed record NuGetProjectDto(
    string ProjectName,
    string ProjectPath,
    IReadOnlyList<NuGetPackageReferenceDto> PackageReferences);

public sealed record NuGetPackageReferenceDto(
    string PackageId,
    string Version);
