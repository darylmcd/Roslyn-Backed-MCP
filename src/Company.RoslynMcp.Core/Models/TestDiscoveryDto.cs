namespace Company.RoslynMcp.Core.Models;

public sealed record TestDiscoveryDto(
    IReadOnlyList<TestProjectDto> TestProjects);

public sealed record TestProjectDto(
    string ProjectName,
    string ProjectFilePath,
    IReadOnlyList<TestCaseDto> Tests);

public sealed record TestCaseDto(
    string DisplayName,
    string FullyQualifiedName,
    string? FilePath,
    int? Line);
