namespace Company.RoslynMcp.Core.Models;

public sealed record WorkspaceStatusDto(
    string? SolutionPath,
    int WorkspaceVersion,
    IReadOnlyList<ProjectStatusDto> Projects,
    bool IsLoaded,
    IReadOnlyList<string>? LoadWarnings);

public sealed record ProjectStatusDto(
    string Name,
    string FilePath,
    int DocumentCount,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework);
