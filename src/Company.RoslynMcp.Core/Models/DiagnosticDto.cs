namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a compiler, analyzer, or workspace diagnostic.
/// </summary>
public sealed record DiagnosticDto(
    string Id,
    string Message,
    string Severity,
    string Category,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn);
