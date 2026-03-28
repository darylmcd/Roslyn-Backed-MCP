namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of an in-memory compilation check without invoking dotnet build.
/// </summary>
public sealed record CompileCheckDto(
    bool Success,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    long ElapsedMs);
