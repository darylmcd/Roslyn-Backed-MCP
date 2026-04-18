namespace RoslynMcp.Core.Services;

/// <summary>
/// Watches the file system for changes in loaded workspace directories
/// and tracks which workspaces have become stale.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>Start watching for changes in the directory containing the loaded workspace.</summary>
    void Watch(string workspaceId, string workspacePath);

    /// <summary>Stop watching a workspace.</summary>
    void Unwatch(string workspaceId);

    /// <summary>Check if a workspace has pending file changes since last load/reload.</summary>
    bool IsStale(string workspaceId);

    /// <summary>
    /// Returns the reason the workspace is stale, or <see langword="null"/> when it is not
    /// stale (or the workspace is unknown). Possible values:
    /// <list type="bullet">
    ///   <item><description><see cref="StaleReasons.ExternalEdit"/> — a tracked file changed
    ///     on disk outside the server's apply channel (e.g. an editor overwrote a <c>.cs</c>
    ///     file). Write-preview tools should refuse and direct the caller to
    ///     <c>workspace_reload</c>.</description></item>
    ///   <item><description><see cref="StaleReasons.Apply"/> — the staleness was recorded by the
    ///     server's own <c>TryApplyChanges</c>/edit path. Auto-reload is safe.</description></item>
    ///   <item><description><see cref="StaleReasons.Restore"/> — an undo / revert path touched
    ///     on-disk files and re-seeded the watcher state. Auto-reload is safe.</description></item>
    /// </list>
    /// </summary>
    string? GetStaleReason(string workspaceId);

    /// <summary>
    /// Programmatically marks the workspace as stale with the given reason. Used by the server
    /// to distinguish its own apply/restore writes from genuinely external edits. Semantics
    /// are <em>last-writer-wins</em> within a single stale window: each call overwrites the
    /// prior reason until <see cref="ClearStale"/> resets the slate.
    /// </summary>
    void MarkStale(string workspaceId, string reason);

    /// <summary>Clear the stale flag (called after reload).</summary>
    void ClearStale(string workspaceId);
}

/// <summary>
/// Canonical string constants for the <c>staleReason</c> field on
/// <see cref="Models.WorkspaceStatusDto"/> and <see cref="IFileWatcherService.GetStaleReason"/>.
/// Kept alongside the interface so host / test code can reference the constants without
/// depending on Roslyn services.
/// </summary>
public static class StaleReasons
{
    /// <summary>
    /// A tracked <c>.cs</c>/<c>.csproj</c>/<c>.props</c>/<c>.targets</c>/<c>.sln</c>/<c>.slnx</c>
    /// file changed on disk outside the server's apply channel. Set by watcher-driven events
    /// (the default for any file-system change the server did not pre-attribute). This is the
    /// condition write-preview tools (change_signature_preview, move_type_to_file_preview, etc.)
    /// must refuse on — auto-reload would silently swallow the external drift.
    /// </summary>
    public const string ExternalEdit = "external-edit";

    /// <summary>
    /// The workspace became stale because the server itself just applied an edit
    /// (<c>apply_text_edit</c>, <c>apply_multi_file_edit</c>, <c>TryApplyChanges</c>, …).
    /// Auto-reload is safe; write-preview tools do not need to refuse.
    /// </summary>
    public const string Apply = "apply";

    /// <summary>
    /// The workspace became stale because an undo / revert path rewrote files to their
    /// pre-apply contents. Auto-reload is safe.
    /// </summary>
    public const string Restore = "restore";
}
