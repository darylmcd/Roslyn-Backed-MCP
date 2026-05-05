namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of <see cref="Services.IWorkspaceDriftService.CheckDriftAsync"/>: the diff
/// between the in-memory MSBuildWorkspace snapshot and current on-disk state.
/// </summary>
/// <param name="Stale">
/// <see langword="true"/> when at least one tracked document file's last-write time
/// (UTC) is past the workspace's load timestamp, or the file no longer exists on disk.
/// </param>
/// <param name="FilesDrifted">
/// Absolute paths of documents whose disk state has drifted past the workspace snapshot,
/// sorted alphabetically (case-insensitive on Windows, ordinal). Empty when
/// <paramref name="Stale"/> is <see langword="false"/>.
/// </param>
/// <param name="Recommended">
/// <c>"reload"</c> when drift is detected, <c>"noop"</c> when the workspace is already
/// in sync with disk. Strings are stable contract — agents branch on the literal value.
/// </param>
public sealed record WorkspaceDriftResult(
    bool Stale,
    IReadOnlyList<string> FilesDrifted,
    string Recommended);
