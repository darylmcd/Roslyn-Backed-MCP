namespace RoslynMcp.Core.Services;

/// <summary>
/// Serializes execution per workspace so that multiple concurrent callers (e.g. subagents)
/// do not corrupt Roslyn state. Different workspaces always run independently of each other.
/// </summary>
/// <remarks>
/// Prefer <see cref="RunReadAsync{T}"/> and <see cref="RunWriteAsync{T}"/> in new code: callers
/// classify themselves as readers (analysis, navigation, search, diagnostics) or writers
/// (refactor apply, code-fix apply, project mutation, file CRUD). Under the legacy code path
/// both methods serialize behind a per-workspace mutex; when the reader/writer lock feature
/// flag is enabled (<c>ROSLYNMCP_WORKSPACE_RW_LOCK</c>), reads run concurrently against the
/// same workspace and writes are exclusive against in-flight reads.
///
/// <para>
/// <b>No reentrance.</b> Actions passed to <see cref="RunReadAsync{T}"/> or
/// <see cref="RunWriteAsync{T}"/> must not invoke another gate method against the same
/// workspace id from inside the action. Reentrance can deadlock the per-workspace lock; the
/// per-request timeout is the only backstop.
/// </para>
///
/// <para>
/// <see cref="RunAsync{T}"/> remains as a thin compatibility shim that routes by string prefix
/// to the explicit methods. New code should not call it; it exists so the migration to the
/// reader/writer model can land incrementally.
/// </para>
/// </remarks>
public interface IWorkspaceExecutionGate
{
    /// <summary>
    /// The well-known gate key used for workspace_load and workspace_reload operations.
    /// </summary>
    const string LoadGateKey = "__load__";

    /// <summary>
    /// Run an async <b>read</b> action against the given workspace. Multiple concurrent reads
    /// against the same workspace are permitted under the reader/writer lock feature flag;
    /// otherwise reads serialize behind the legacy per-workspace mutex. Reads block writers
    /// and are blocked by writers when the flag is enabled.
    /// </summary>
    /// <remarks>
    /// The action must not call another gate method against the same workspace id (no reentrance).
    /// </remarks>
    Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct);

    /// <summary>
    /// Run an async <b>write</b> action against the given workspace. Writes are exclusive
    /// against all other operations (reads and writes) on the same workspace.
    /// </summary>
    /// <remarks>
    /// The action must not call another gate method against the same workspace id (no reentrance).
    /// </remarks>
    Task<T> RunWriteAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct);

    /// <summary>
    /// Run an async action while holding the gate for the given string key.
    /// Use <see cref="LoadGateKey"/> for workspace_load and workspace_reload.
    /// </summary>
    /// <remarks>
    /// Legacy compatibility shim. New code should call <see cref="RunReadAsync{T}"/> or
    /// <see cref="RunWriteAsync{T}"/> directly. This method routes by string prefix:
    /// <list type="bullet">
    /// <item><see cref="LoadGateKey"/> uses the shared load gate.</item>
    /// <item><c>__apply__:&lt;workspaceId&gt;</c> routes to <see cref="RunWriteAsync{T}"/>.</item>
    /// <item>A bare workspace id routes to <see cref="RunReadAsync{T}"/>.</item>
    /// </list>
    /// </remarks>
    Task<T> RunAsync<T>(string? gateKey, Func<CancellationToken, Task<T>> action, CancellationToken ct);

    /// <summary>
    /// Remove and dispose the gate for a workspace that is being closed.
    /// </summary>
    /// <remarks>
    /// The caller must already hold the per-workspace write lock when the reader/writer lock
    /// flag is enabled, so that no in-flight reader is operating against the workspace when its
    /// lock entry is dropped. <c>workspace_close</c> in <c>WorkspaceTools</c> handles the
    /// double-acquire (load gate → per-workspace write lock → close → RemoveGate).
    /// </remarks>
    void RemoveGate(string workspaceId);
}
