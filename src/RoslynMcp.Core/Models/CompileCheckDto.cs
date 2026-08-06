namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of an in-memory compilation check without invoking dotnet build.
/// </summary>
/// <param name="Success">True when there are zero errors, the run was not cancelled, and at least
/// one project was evaluated (<c>CompletedProjects &gt; 0</c>). A vacuous result (e.g. zero
/// projects after filtering) is <see langword="false"/> even when <see cref="ErrorCount"/> is 0.</param>
/// <param name="ErrorCount">Total error count across the unfiltered solution (or filtered scope).</param>
/// <param name="WarningCount">Total warning count across the unfiltered solution (or filtered scope).</param>
/// <param name="TotalDiagnostics">Total diagnostics matching the filters before pagination is applied.</param>
/// <param name="ReturnedDiagnostics">Number of diagnostics actually included in <see cref="Diagnostics"/> after offset/limit slicing.</param>
/// <param name="Offset">The pagination offset that was applied.</param>
/// <param name="Limit">The pagination limit that was applied.</param>
/// <param name="HasMore">True when more diagnostics are available beyond the returned page.</param>
/// <param name="Diagnostics">The page of diagnostics returned by this call.</param>
/// <param name="ElapsedMs">Wall-clock time spent in the call, in milliseconds.</param>
/// <param name="RequestedScope">The compile scope implied by the caller's arguments, one of
/// <c>"files"</c> (a <c>file</c>/<c>files</c> filter was supplied), <c>"project"</c> (a
/// <c>projectName</c> filter was supplied — takes precedence over a file filter), or
/// <c>"solution"</c> (no scoping filter). Additive and non-breaking: <see langword="null"/>
/// on responses produced by paths that do not compute a scope.</param>
/// <param name="ActualScope">The compile scope that actually ran, drawn from the same
/// <c>"files"</c>/<c>"project"</c>/<c>"solution"</c> vocabulary as <see cref="RequestedScope"/>.
/// When a <c>files</c> filter spans zero or more than one owning project the check silently widens
/// to every project in the solution, so <see cref="ActualScope"/> becomes <c>"solution"</c> while
/// <see cref="RequestedScope"/> stays <c>"files"</c> — comparing the two is the structured
/// (non-prose) way to detect that fallback, which is otherwise only reported inside
/// <see cref="RestoreHint"/>. Additive and non-breaking.</param>
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
    int? TotalProjects = null,
    string? RequestedScope = null,
    string? ActualScope = null);
