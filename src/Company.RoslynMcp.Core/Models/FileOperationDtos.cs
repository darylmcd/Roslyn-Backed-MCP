namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a request to create a file in a project.
/// </summary>
public sealed record CreateFileDto(
    string ProjectName,
    string FilePath,
    string Content);

/// <summary>
/// Represents a request to delete a file.
/// </summary>
public sealed record DeleteFileDto(
    string FilePath);

/// <summary>
/// Represents a request to move a file and optionally update its namespace.
/// </summary>
public sealed record MoveFileDto(
    string SourceFilePath,
    string DestinationFilePath,
    string? DestinationProjectName,
    bool UpdateNamespace);