namespace Company.RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <c>WorkspaceManager</c> session limits.
/// </summary>
public sealed class WorkspaceManagerOptions
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
}
