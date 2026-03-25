namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a request to add a package reference to a project.
/// </summary>
public sealed record AddPackageReferenceDto(
    string ProjectName,
    string PackageId,
    string Version);

/// <summary>
/// Represents a request to remove a package reference from a project.
/// </summary>
public sealed record RemovePackageReferenceDto(
    string ProjectName,
    string PackageId);

/// <summary>
/// Represents a request to add a project-to-project reference.
/// </summary>
public sealed record AddProjectReferenceDto(
    string ProjectName,
    string ReferencedProjectName);

/// <summary>
/// Represents a request to remove a project-to-project reference.
/// </summary>
public sealed record RemoveProjectReferenceDto(
    string ProjectName,
    string ReferencedProjectName);

/// <summary>
/// Represents a request to set a project property.
/// </summary>
public sealed record SetProjectPropertyDto(
    string ProjectName,
    string PropertyName,
    string Value);

/// <summary>
/// Represents a request to add a target framework to a project.
/// </summary>
public sealed record AddTargetFrameworkDto(
    string ProjectName,
    string TargetFramework);

/// <summary>
/// Represents a request to remove a target framework from a project.
/// </summary>
public sealed record RemoveTargetFrameworkDto(
    string ProjectName,
    string TargetFramework);

/// <summary>
/// Represents a request to set a conditional project property.
/// </summary>
public sealed record SetConditionalPropertyDto(
    string ProjectName,
    string PropertyName,
    string Value,
    string Condition);

/// <summary>
/// Represents a request to add a centrally managed package version.
/// </summary>
public sealed record AddCentralPackageVersionDto(
    string PackageId,
    string Version);

/// <summary>
/// Represents a request to remove a centrally managed package version.
/// </summary>
public sealed record RemoveCentralPackageVersionDto(
    string PackageId);