namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of an in-memory compilation check without invoking dotnet build.
/// </summary>
/// <param name="Success">True when there are zero errors across the unfiltered total.</param>
/// <param name="ErrorCount">Total error count across the unfiltered solution (or filtered scope).</param>
/// <param name="WarningCount">Total warning count across the unfiltered solution (or filtered scope).</param>
/// <param name="TotalDiagnostics">Total diagnostics matching the filters before pagination is applied.</param>
/// <param name="ReturnedDiagnostics">Number of diagnostics actually included in <see cref="Diagnostics"/> after offset/limit slicing.</param>
/// <param name="Offset">The pagination offset that was applied.</param>
/// <param name="Limit">The pagination limit that was applied.</param>
/// <param name="HasMore">True when more diagnostics are available beyond the returned page.</param>
/// <param name="Diagnostics">The page of diagnostics returned by this call.</param>
/// <param name="ElapsedMs">Wall-clock time spent in the call, in milliseconds.</param>
public sealed record CompileCheckDto(
    bool Success,
    int ErrorCount,
    int WarningCount,
    int TotalDiagnostics,
    int ReturnedDiagnostics,
    int Offset,
    int Limit,
    bool HasMore,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    long ElapsedMs,
    string? RestoreHint = null,
    bool Cancelled = false,
    int? CompletedProjects = null,
    int? TotalProjects = null);
