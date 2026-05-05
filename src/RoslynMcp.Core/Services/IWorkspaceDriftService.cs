using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Compares an in-memory MSBuildWorkspace snapshot against filesystem mtimes so callers
/// can branch — invoke <c>workspace_reload</c> only when the on-disk state has actually
/// drifted past the workspace's load timestamp instead of either always-reloading
/// (slow) or never-reloading (silent stale reads after out-of-band <c>Edit</c>/<c>Write</c>
/// mutations).
/// </summary>
/// <remarks>
/// <para>
/// The drift check compares <see cref="System.IO.File.GetLastWriteTimeUtc"/> for every
/// document in the workspace's solution against the workspace's <c>LoadedAtUtc</c>
/// timestamp from <see cref="WorkspaceStatusDto"/>. Files whose mtime exceeds the load
/// time, or whose path no longer exists on disk, are reported as drifted.
/// </para>
/// <para>
/// This is intentionally a thin, deterministic probe — no semantic-model resolution, no
/// project loading. Callers use the resulting recommendation to decide whether to take
/// the cost of <c>workspace_reload</c> before issuing a read.
/// </para>
/// </remarks>
public interface IWorkspaceDriftService
{
    /// <summary>
    /// Probes the loaded workspace for drift between its in-memory snapshot and the
    /// current filesystem state.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by <c>workspace_load</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WorkspaceDriftResult"/> with <c>Stale=false</c> and
    /// <c>Recommended="noop"</c> when no document file's mtime exceeds the workspace's
    /// load timestamp. Otherwise <c>Stale=true</c>, <c>FilesDrifted</c> populated with
    /// the affected paths in stable alphabetical order, and <c>Recommended="reload"</c>.
    /// </returns>
    Task<WorkspaceDriftResult> CheckDriftAsync(string workspaceId, CancellationToken ct);
}
