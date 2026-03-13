namespace Company.RoslynMcp.Core.Models;

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
