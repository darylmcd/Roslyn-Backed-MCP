using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the current status of a loaded workspace.
/// </summary>
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
    bool IsStale,
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics);

/// <summary>
/// Represents the status of a project within a loaded workspace.
/// </summary>
public sealed record ProjectStatusDto(
    [property: JsonPropertyName("ProjectName")]
    string Name,
    string FilePath,
    int DocumentCount,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> TargetFrameworks,
    bool IsTestProject,
    string AssemblyName,
    string OutputType);
