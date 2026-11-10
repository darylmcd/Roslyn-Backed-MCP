namespace RoslynMcp.Core.Services;

/// <summary>
/// Watches the file system for changes in loaded workspace directories
/// and tracks which workspaces have become stale.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Raised when the directory containing a watched workspace disappears. Implementations
    /// that do not support root lifecycle signals may retain the default no-op accessors.
    /// </summary>
    event Action<string>? WorkspaceRootMissing
    {
        add { }
        remove { }
    }

    /// <summary>Start watching for changes in the directory containing the loaded workspace.</summary>
    void Watch(string workspaceId, string workspacePath);

    /// <summary>Stop watching a workspace.</summary>
    void Unwatch(string workspaceId);

    /// <summary>Check if a workspace has pending file changes since last load/reload.</summary>
    bool IsStale(string workspaceId);

    /// <summary>
    /// Returns a task that completes as soon as the workspace is (or becomes) stale. If the
    /// workspace is already stale the returned task is already completed; otherwise it completes
    /// the next time the staleness flag flips (watcher event or an explicit
    /// <see cref="MarkStale"/>). Lets callers await the actual staleness signal instead of
    /// polling, removing the wall-clock race against the OS-level file-system event dispatcher.
    /// </summary>
    /// <remarks>
    /// The task is canceled (faulted with <see cref="OperationCanceledException"/>) if
    /// <paramref name="ct"/> fires. An unknown workspace returns an already-completed task — a
    /// caller that has not yet started watching cannot wait for a signal that will never arrive.
    /// </remarks>
    Task WaitForStaleAsync(string workspaceId, CancellationToken ct);

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

    /// <summary>Clear the stale flag unconditionally.</summary>
    void ClearStale(string workspaceId);

    /// <summary>
    /// Clears the stale flag unless a <see cref="StaleReasons.ExternalEdit"/> mark records a change
    /// observed at or after <paramref name="sinceUtc"/>. A reload passes the UTC instant it captured
    /// BEFORE it started reading disk: an edit made at or after that instant may postdate the bytes
    /// the load read, so the workspace must stay stale. A change whose write finished earlier (the
    /// edit the reload was invoked to absorb, a duplicate watcher event for it, or the delayed echo
    /// of the server's own write) is already covered by the load and is cleared.
    /// </summary>
    /// <remarks>
    /// The "observed" instant of a content event (changed/created) is the file's last-write time;
    /// of a delete or rename, or of an explicit <see cref="MarkStale"/>, the instant the mark was
    /// recorded. Ties survive (<c>&gt;=</c>), and a file whose time cannot be read counts as newly
    /// observed, so every ambiguity resolves to "stay stale". <see cref="StaleReasons.Apply"/> and
    /// <see cref="StaleReasons.Restore"/> marks never keep the workspace stale here: the server's
    /// own writes are on disk before a reload starts. Returns <see langword="true"/> when the flag
    /// was cleared; <see langword="false"/> when a newer external edit intervened (nothing changes:
    /// flag, reason and pending <see cref="WaitForStaleAsync"/> waiters are left exactly as they were)
    /// or the workspace is unknown.
    /// </remarks>
    bool ClearStaleIfNoExternalEditSince(string workspaceId, DateTime sinceUtc);
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
