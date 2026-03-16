namespace Company.RoslynMcp.Core.Services;

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

    /// <summary>Clear the stale flag (called after reload).</summary>
    void ClearStale(string workspaceId);
}
