namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <c>WorkspaceManager</c> session limits.
/// </summary>
public sealed record WorkspaceManagerOptions
{
    /// <summary>
    /// Gets the maximum number of workspace sessions that may be open concurrently.
    /// Defaults to 8.
    /// </summary>
    public int MaxConcurrentWorkspaces { get; init; } = 8;

    /// <summary>
    /// Gets the maximum number of source-generated documents to return per workspace query.
    /// Defaults to 500.
    /// </summary>
    public int MaxSourceGeneratedDocuments { get; init; } = 500;

    /// <summary>
    /// Gets the upper bound (in milliseconds) that <see cref="WorkspaceManager.LoadAsync"/>
    /// will wait for a concurrent out-of-process <c>dotnet restore</c> to produce stable
    /// <c>obj/project.assets.json</c> / <c>obj/*.dgspec.json</c> mtimes before snapshotting
    /// the workspace. Defaults to 2000 ms. A value of <c>0</c> disables the wait entirely
    /// (legacy pre-fix behaviour). Bound by <c>ROSLYNMCP_RESTORE_RACE_WAIT_MS</c> at
    /// bootstrap; see <c>dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz</c>.
    /// </summary>
    public int RestoreRaceWaitMs { get; init; } = 2000;
}
