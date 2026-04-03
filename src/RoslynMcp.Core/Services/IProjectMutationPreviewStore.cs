namespace RoslynMcp.Core.Services;

/// <summary>
/// Stores and retrieves pending project file mutation previews between a preview call and its
/// corresponding apply call.
/// </summary>
/// <remarks>
/// Entries expire after a fixed TTL. The store is bounded to prevent unbounded memory growth.
/// </remarks>
public interface IProjectMutationPreviewStore
{
    /// <summary>
    /// Stores a pending project file mutation and returns an opaque preview token.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilePath">The absolute path to the project file being mutated.</param>
    /// <param name="updatedContent">The new project file XML content.</param>
    /// <param name="workspaceVersion">The workspace version at the time the preview was computed.</param>
    /// <param name="description">A human-readable description of the pending operation.</param>
    /// <returns>An opaque token that can be passed to <see cref="Retrieve"/> or <see cref="Invalidate"/>.</returns>
    string Store(string workspaceId, string projectFilePath, string updatedContent, int workspaceVersion, string description);

    /// <summary>
    /// Retrieves the stored mutation for the given token, or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    (string WorkspaceId, string ProjectFilePath, string UpdatedContent, int WorkspaceVersion, string Description)? Retrieve(string token);

    /// <summary>
    /// Removes the entry for the given token.
    /// </summary>
    void Invalidate(string token);

    /// <summary>
    /// Returns the workspace identifier associated with a preview token without consuming the entry,
    /// or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    string? PeekWorkspaceId(string token);
}
