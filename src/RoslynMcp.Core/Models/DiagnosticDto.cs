namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a compiler, analyzer, or workspace diagnostic.
/// <see cref="FilePath"/> is the file the diagnostic is about (null when no file is known) and is set
/// whenever a file is known, including diagnostics that have no position (for example a NuGet error
/// against a project file). <see cref="Location"/> carries the span and is non-null only when both a
/// path and a start position exist; when present, its path equals <see cref="FilePath"/>.
/// </summary>
public sealed record DiagnosticDto(
    string Id,
    string Message,
    string Severity,
    string Category,
    string? FilePath,
    LocationDto? Location = null);
