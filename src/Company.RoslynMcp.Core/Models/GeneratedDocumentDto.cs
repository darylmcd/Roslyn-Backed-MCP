namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a generated document emitted for a project, such as a source generator output.
/// </summary>
public sealed record GeneratedDocumentDto(
    string ProjectName,
    string HintName,
    string FilePath);
