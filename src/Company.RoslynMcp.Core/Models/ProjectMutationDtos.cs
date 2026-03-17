namespace Company.RoslynMcp.Core.Models;

public sealed record AddPackageReferenceDto(
    string ProjectName,
    string PackageId,
    string Version);

public sealed record RemovePackageReferenceDto(
    string ProjectName,
    string PackageId);

public sealed record AddProjectReferenceDto(
    string ProjectName,
    string ReferencedProjectName);

public sealed record RemoveProjectReferenceDto(
    string ProjectName,
    string ReferencedProjectName);

public sealed record SetProjectPropertyDto(
    string ProjectName,
    string PropertyName,
    string Value);

public sealed record AddTargetFrameworkDto(
    string ProjectName,
    string TargetFramework);

public sealed record RemoveTargetFrameworkDto(
    string ProjectName,
    string TargetFramework);

public sealed record SetConditionalPropertyDto(
    string ProjectName,
    string PropertyName,
    string Value,
    string Condition);

public sealed record AddCentralPackageVersionDto(
    string PackageId,
    string Version);

public sealed record RemoveCentralPackageVersionDto(
    string PackageId);