namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a source location where a property is written.
/// </summary>
public sealed record PropertyWriteDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText,
    bool IsObjectInitializer);
