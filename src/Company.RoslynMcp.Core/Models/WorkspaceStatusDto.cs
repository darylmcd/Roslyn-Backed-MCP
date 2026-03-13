namespace Company.RoslynMcp.Core.Models;

public sealed record WorkspaceStatusDto(
    string WorkspaceId,
    string? LoadedPath,
    int WorkspaceVersion,
    string SnapshotToken,
    DateTimeOffset LoadedAtUtc,
    int ProjectCount,
    int DocumentCount,
    IReadOnlyList<ProjectStatusDto> Projects,
    bool IsLoaded,
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics);

public sealed record ProjectStatusDto(
    string Name,
    string FilePath,
    int DocumentCount,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> TargetFrameworks,
    bool IsTestProject,
    string AssemblyName,
    string OutputType);
