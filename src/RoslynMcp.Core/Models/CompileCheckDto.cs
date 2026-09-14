namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of an in-memory compilation check without invoking dotnet build.
/// </summary>
/// <param name="Success">True when there are zero errors, the run was not cancelled, and at least
/// one project was evaluated and all selected projects completed
/// (<c>CompletedProjects &gt; 0 &amp;&amp; CompletedProjects == TotalProjects</c>). A vacuous result (e.g. zero
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
/// <see cref="ScopeFiles"/> (a <c>file</c>/<c>files</c> filter was supplied), <see cref="ScopeProject"/> (a
/// <c>projectName</c> filter was supplied — takes precedence over a file filter), or
/// <see cref="ScopeSolution"/> (no scoping filter). Additive and non-breaking: <see langword="null"/>
/// on responses produced by paths that do not compute a scope.</param>
/// <param name="ActualScope">The compile scope that actually ran, drawn from the same
/// vocabulary as <see cref="RequestedScope"/>.
/// A file scope compiles only its owning projects and filters diagnostics to the requested paths.
/// Unresolved file scopes compile nothing. Check <see cref="CompletedProjects"/> and
/// <see cref="Readiness"/> to distinguish unevaluated results.</param>
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
    string? ActualScope = null)
{
    /// <summary>Only projects owning requested files are selected.</summary>
    public const string ScopeFiles = "files";

    /// <summary>An explicit project filter takes precedence over file-based selection.</summary>
    public const string ScopeProject = "project";

    /// <summary>All loaded projects are selected.</summary>
    public const string ScopeSolution = "solution";

    /// <summary>
    /// Package/analyzer readiness: <c>ready</c>, <c>restore-required</c> (no diagnostics
    /// evaluated), or <c>analyzer-limited</c> (compiler diagnostics remain available).
    /// Independent of <see cref="Success"/>; ready results may contain compilation errors.
    /// </summary>
    public string Readiness { get; init; } = "ready";
}
