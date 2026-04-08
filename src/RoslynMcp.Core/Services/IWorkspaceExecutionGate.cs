namespace RoslynMcp.Core.Services;

/// <summary>
/// Serializes execution per workspace so that multiple concurrent callers (e.g. subagents)
/// do not corrupt Roslyn state. Different workspaces always run independently of each other.
/// </summary>
/// <remarks>
/// Callers classify themselves as readers (analysis, navigation, search, diagnostics) or
/// writers (refactor apply, code-fix apply, project mutation, file CRUD). Reads against the
/// same workspace run concurrently; writes are exclusive against all other operations on the
/// same workspace.
///
/// <para>
/// <b>No reentrance.</b> Actions passed to <see cref="RunReadAsync{T}"/> or
/// <see cref="RunWriteAsync{T}"/> must not invoke another gate method against the same
/// workspace id from inside the action. Reentrance can deadlock the per-workspace lock; the
/// per-request timeout is the only backstop.
/// </para>
///
/// <para>
/// Workspace lifecycle operations (<c>workspace_load</c>, <c>workspace_reload</c>,
/// <c>workspace_close</c>) acquire <see cref="RunLoadGateAsync{T}"/> as the outer scope.
/// Reload and close additionally nest a <see cref="RunWriteAsync{T}"/> on the per-workspace
/// lock so any in-flight readers complete before the workspace state is replaced or removed.
/// </para>
/// </remarks>
public interface IWorkspaceExecutionGate
{
    /// <summary>
    /// Run an async <b>read</b> action against the given workspace. Multiple concurrent reads
    /// against the same workspace are permitted; reads block writers and are blocked by writers.
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
    /// Run an async action under the global <b>load gate</b>. Used by <c>workspace_load</c>,
    /// <c>workspace_reload</c>, and <c>workspace_close</c> to serialize lifecycle changes that
    /// would otherwise race against each other across workspaces.
    /// </summary>
    /// <remarks>
    /// Reload and close nest a <see cref="RunWriteAsync{T}"/> on the per-workspace lock inside
    /// the load gate so in-flight readers on the affected workspace complete before its state
    /// is replaced or removed.
    /// </remarks>
    Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);

    /// <summary>
    /// Remove and dispose the gate for a workspace that is being closed.
    /// </summary>
    /// <remarks>
    /// The caller must already hold the per-workspace write lock so that no in-flight reader is
    /// operating against the workspace when its lock entry is dropped from the registry.
    /// <c>workspace_close</c> in <c>WorkspaceTools</c> handles the double-acquire (load gate →
    /// per-workspace write lock → close → RemoveGate).
    /// </remarks>
    void RemoveGate(string workspaceId);
}
