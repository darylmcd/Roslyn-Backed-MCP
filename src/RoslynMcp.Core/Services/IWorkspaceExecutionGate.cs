namespace RoslynMcp.Core.Services;

/// <summary>
/// Serializes execution per workspace so that multiple concurrent callers (e.g. subagents)
/// do not corrupt Roslyn state. One operation per workspace at a time; different workspaces can run in parallel.
/// Use the load gate for workspace_load and workspace_reload.
/// </summary>
public interface IWorkspaceExecutionGate
{
/// <summary>
/// Run an async action while holding the gate for the given key.
/// Use the load gate key (e.g. "__load__") for workspace_load and workspace_reload.
/// Use the workspace session id for all other tools that take a workspaceId.
/// </summary>
    Task<T> RunAsync<T>(string? gateKey, Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
