namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a compiler, analyzer, or workspace diagnostic. <see cref="Location"/> is null only when the
/// diagnostic has no file and start position (project- or solution-level findings).
/// </summary>
public sealed record DiagnosticDto(
    string Id,
    string Message,
    string Severity,
    string Category,
    LocationDto? Location = null);
