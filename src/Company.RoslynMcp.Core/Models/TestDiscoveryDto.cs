namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents discovered test projects and test cases.
/// </summary>
public sealed record TestDiscoveryDto(
    IReadOnlyList<TestProjectDto> TestProjects);

/// <summary>
/// Represents a test project and its discovered test cases.
/// </summary>
public sealed record TestProjectDto(
    string ProjectName,
    string ProjectFilePath,
    IReadOnlyList<TestCaseDto> Tests);

/// <summary>
/// Represents a discovered test case.
/// </summary>
public sealed record TestCaseDto(
    string DisplayName,
    string FullyQualifiedName,
    string? FilePath,
    int? Line);
