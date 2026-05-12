namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// Controls how <see cref="RoslynMcp.Roslyn.Contracts.IWorkspaceManager.LoadAsync"/> behaves when
/// the concurrent-workspace cap (<c>ROSLYNMCP_MAX_WORKSPACES</c>) is reached.
/// </summary>
public enum EvictPolicy
{
    /// <summary>
    /// Throw <see cref="System.InvalidOperationException"/> when the cap is reached.
    /// The error message includes the list of active workspaces and the least-recently-used
    /// candidate so callers can perform one-round-trip self-recovery. This is the default.
    /// </summary>
    Strict,

    /// <summary>
    /// Silently evict the least-recently-used workspace (the one with the smallest
    /// <c>LastAccessedUtc</c>) and proceed with the new load. Locked sessions that are
    /// currently executing a read or write operation are skipped during the LRU scan; if
    /// all sessions are locked the behaviour falls back to <see cref="Strict"/>.
    /// </summary>
    Lru,
}
