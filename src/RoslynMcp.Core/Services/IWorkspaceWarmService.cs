using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Opt-in compilation prewarm for a loaded workspace. Forces Roslyn's
/// <c>GetCompilationAsync</c> and semantic-model resolution across the workspace (or a
/// filtered project set) so the next read-side tool call (e.g. <c>symbol_search</c>,
/// <c>find_references</c>) does not pay the full cold-start penalty (~4600ms on a
/// mid-sized solution).
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in only.</b> Per the 2026-04-14 user preference against blocking tool
/// registration, warming never runs automatically on <c>workspace_load</c>. Callers that
/// prefer cold-start latency continue to pay it on the first read; callers that want
/// the warm-start profile invoke <see cref="WarmAsync"/> explicitly after load.
/// </para>
/// <para>
/// <b>No default timeout.</b> Warming all projects in a large solution can take tens
/// of seconds. The returned <see cref="WorkspaceWarmResult.ElapsedMs"/> lets the caller
/// budget future invocations; the service itself will not time out or cancel mid-project
/// except via the caller's <see cref="CancellationToken"/>.
/// </para>
/// </remarks>
public interface IWorkspaceWarmService
{
    /// <summary>
    /// Force-compile every project in the workspace (or the subset named by
    /// <paramref name="projects"/>) and resolve one semantic model per project to stabilize
    /// the semantic cache. Compilations already present in Roslyn's internal cache are
    /// counted as warm; each cold project's first <c>GetCompilationAsync</c> is what delivers
    /// the latency delta.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by <c>workspace_load</c>.</param>
    /// <param name="projects">
    /// Optional filter of project names (case-insensitive). When <see langword="null"/> or
    /// empty every project in the solution is warmed. Unknown names are silently skipped so
    /// callers can pass a best-effort set without pre-validation.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    Task<WorkspaceWarmResult> WarmAsync(string workspaceId, string[]? projects, CancellationToken ct);
}
