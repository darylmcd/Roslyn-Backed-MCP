namespace RoslynMcp.Core.Services;

/// <summary>
/// Stores and retrieves composite file mutation previews that involve changes to multiple files
/// without requiring a Roslyn <c>Solution</c> snapshot.
/// </summary>
/// <remarks>
/// Tokens expire after a fixed TTL and become invalid if the workspace version advances.
/// </remarks>
public interface ICompositePreviewStore
{
    /// <summary>
    /// Stores a set of composite file mutations and returns an opaque preview token.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier the mutations apply to.</param>
    /// <param name="workspaceVersion">The workspace version at the time the preview was created.</param>
    /// <param name="description">A human-readable description of the operation.</param>
    /// <param name="mutations">The ordered list of file mutations to preview.</param>
    /// <returns>An opaque token that can be passed to <see cref="Retrieve"/> or <see cref="Invalidate"/>.</returns>
    string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations);

    /// <summary>
    /// Retrieves the stored mutation set for the given token, or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token);

    /// <summary>
    /// Removes the entry for the given token.
    /// </summary>
    void Invalidate(string token);

    /// <summary>
    /// Removes all entries, optionally scoped to a specific workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace to clear, or <see langword="null"/> to clear all workspaces.</param>
    void InvalidateAll(string? workspaceId = null);
}

/// <summary>
/// Describes a single file mutation within a composite preview: either an in-place content update or a deletion.
/// </summary>
/// <param name="FilePath">The absolute path to the file being mutated.</param>
/// <param name="UpdatedContent">The new file content, or <see langword="null"/> if the file is being deleted.</param>
/// <param name="DeleteFile">When <see langword="true"/>, the file will be deleted; <paramref name="UpdatedContent"/> is ignored.</param>
public sealed record CompositeFileMutation(
    string FilePath,
    string? UpdatedContent,
    bool DeleteFile = false);