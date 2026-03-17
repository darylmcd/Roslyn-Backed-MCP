namespace Company.RoslynMcp.Core.Models;

public sealed record CreateFileDto(
    string ProjectName,
    string FilePath,
    string Content);

public sealed record DeleteFileDto(
    string FilePath);

public sealed record MoveFileDto(
    string SourceFilePath,
    string DestinationFilePath,
    string? DestinationProjectName,
    bool UpdateNamespace);