namespace Company.RoslynMcp.Core.Models;

public sealed record ProjectGraphDto(
    string WorkspaceId,
    IReadOnlyList<ProjectGraphNodeDto> Projects);

public sealed record ProjectGraphNodeDto(
    string ProjectName,
    string FilePath,
    string AssemblyName,
    bool IsTestProject,
    string OutputType,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> ProjectReferences);
