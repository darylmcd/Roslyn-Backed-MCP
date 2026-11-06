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
    /// Retrieves (and, for a persisted store, claims) the stored mutation set for the given token, or
    /// <see langword="null"/> if the token is expired or not found.
    /// </summary>
    /// <param name="token">The preview token.</param>
    /// <param name="gatedWorkspaceId">
    /// The local workspace session the caller took its write gate on, or <see langword="null"/> when
    /// no gate was taken. A persisted store re-resolves the record's session under its claim lock and,
    /// when the result differs, refuses with <c>PreviewTokenStaleException</c> BEFORE consuming the
    /// token, so a session change between the pre-claim peek and the claim cannot burn it.
    /// </param>
    /// <returns>
    /// The entry, whose <c>WorkspaceId</c> is the LOCAL session id even when a persisted record was
    /// created by another host. A persisted store throws <c>PublicInvalidOperationException</c>, without
    /// consuming the token, when this host has no (or more than one) matching session.
    /// </returns>
    (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token, string? gatedWorkspaceId);

    /// <summary>
    /// Removes the entry for the given token.
    /// </summary>
    void Invalidate(string token);

    /// <summary>
    /// Returns the workspace identifier associated with a preview token without consuming the entry,
    /// or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    string? PeekWorkspaceId(string token);

    /// <summary>
    /// Returns the file path of every mutation a redemption of <paramref name="token"/> would write,
    /// without consuming the entry. A store that persists tokens reads the persisted record — the
    /// same authority <see cref="Retrieve"/> claims — never the creator's in-memory copy.
    /// </summary>
    /// <returns>
    /// The write set, or <see langword="null"/> when the token is expired, unknown, already claimed,
    /// or its persisted record cannot be read. <see langword="null"/> means the write set is
    /// <b>unknown</b>: the caller MUST refuse the redemption (fail closed). This deliberately differs
    /// from the "null means skip" contract of <c>IPreviewStore.PeekChangedPaths</c>, which has no
    /// persisted, separately writable record behind it.
    /// </returns>
    IReadOnlyList<string>? PeekChangedPaths(string token);
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
