namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a preview of applying a code fix to all instances of a diagnostic across a scope.
/// </summary>
public sealed record FixAllPreviewDto(
    string PreviewToken,
    string DiagnosticId,
    string Scope,
    int FixedCount,
    IReadOnlyList<FileChangeDto> Changes,
    string? GuidanceMessage = null);
