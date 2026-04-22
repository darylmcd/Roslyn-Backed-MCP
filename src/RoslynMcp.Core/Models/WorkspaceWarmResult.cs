namespace RoslynMcp.Core.Models;

/// <summary>
/// Result envelope for <c>workspace_warm</c>. Reports the per-project warm roster, the
/// wall-clock budget the warm consumed, and how many projects were cold at the start of
/// the call so callers can verify whether warming actually did work (repeat calls on a
/// warm workspace will report <see cref="ColdCompilationCount"/> = 0).
/// </summary>
/// <param name="WorkspaceId">The workspace session identifier that was warmed.</param>
/// <param name="ProjectsWarmed">
/// Names of the projects whose compilation + first semantic model were force-resolved in
/// this call. Includes projects that were already warm (no-op delta) so the caller sees
/// the full roster the warm inspected. If the per-workspace read lock could not be
/// acquired for a given project the list includes a best-effort note for that project
/// (e.g. a status suffix) rather than silently skipping it.
/// </param>
/// <param name="ElapsedMs">Wall-clock time spent inside <c>WarmAsync</c>, in milliseconds.</param>
/// <param name="ColdCompilationCount">
/// Number of projects whose compilation was NOT yet present in Roslyn's internal cache
/// at the start of the call — i.e. the projects that actually paid the cold-start cost
/// this invocation. A second call on the same workspace with no intervening
/// <c>workspace_reload</c> should report 0.
/// </param>
public sealed record WorkspaceWarmResult(
    string WorkspaceId,
    IReadOnlyList<string> ProjectsWarmed,
    long ElapsedMs,
    int ColdCompilationCount);
