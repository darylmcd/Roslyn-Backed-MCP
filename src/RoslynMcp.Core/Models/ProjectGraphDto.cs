using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the project graph for a loaded workspace.
/// </summary>
public sealed record ProjectGraphDto(
    string WorkspaceId,
    IReadOnlyList<ProjectGraphNodeDto> Projects);

/// <summary>
/// Represents a project node within a workspace project graph.
/// </summary>
public sealed record ProjectGraphNodeDto(
    [property: JsonPropertyName("name")]
    string ProjectName,
    string FilePath,
    string AssemblyName,
    bool IsTestProject,
    string OutputType,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> ProjectReferences);
