using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// Stores and retrieves pending Roslyn <see cref="Solution"/> previews between a preview call and its
/// corresponding apply call.
/// </summary>
/// <remarks>
/// Entries expire after a fixed TTL. The store is bounded to prevent unbounded memory growth.
/// </remarks>
public interface IPreviewStore
{
    /// <summary>
    /// Stores the exact original and modified snapshots used to compute <paramref name="changes"/>,
    /// the actual displayed bounded diff, and the intended producer family. Implementations must
    /// retain both snapshot identities, truncation safety, and provenance through interface dispatch.
    /// </summary>
    string Store(string workspaceId, Solution originalSolution, Solution modifiedSolution, int workspaceVersion,
        string description, IReadOnlyList<FileChangeDto> changes, PreviewKind kind);

    /// <summary>
    /// Retrieves the stored solution pair for the given token, or <see langword="null"/>
    /// if the token is expired or not found.
    /// </summary>
    /// <remarks>
    /// <b>preview-token-cross-coupling-bundle (BREAKING):</b> the returned tuple now exposes
    /// both <c>OriginalSolution</c> (the workspace snapshot captured at preview time) and
    /// <c>ModifiedSolution</c> (with the preview's edits applied). The apply path MUST
    /// compute the preview's intended diff as
    /// <c>ModifiedSolution.GetChanges(OriginalSolution)</c> and replay only that diff onto
    /// the current workspace solution — this preserves sibling token validity when a
    /// concurrent <c>*_apply</c> has advanced the workspace since the preview was created.
    /// Passing <c>ModifiedSolution</c> directly to <c>Workspace.TryApplyChanges</c> is
    /// incorrect: it would either fail a lineage check or silently undo unrelated sibling
    /// edits by treating newly-added documents as removals.
    /// </remarks>
    (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token);

    /// <summary>
    /// Removes the entry for the given token.
    /// </summary>
    void Invalidate(string token);

    /// <summary>
    /// Removes all entries, optionally scoped to a specific workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace to clear, or <see langword="null"/> to clear all workspaces.</param>
    void InvalidateAll(string? workspaceId = null);

    /// <summary>
    /// <b>format-range-apply-preview-token-lifetime:</b> drops only the entries belonging to
    /// <paramref name="workspaceId"/> whose pinned workspace-version range no longer covers
    /// <paramref name="newWorkspaceVersion"/>. Each entry is pinned at Store time to a range
    /// <c>[StoreVersion, StoreVersion + MaxVersionSpan]</c>; an auto-reload that nudges the
    /// workspace version by one bump leaves tokens in the range valid (so a preview → reload
    /// → apply sequence that fits inside a single version bump still redeems), while a second
    /// reload pushes the version past the pinned ceiling and the token is dropped.
    /// </summary>
    /// <remarks>
    /// Replaces the prior "wipe-on-reload" behavior at
    /// <c>WorkspaceManager.LoadIntoSessionAsync</c>, which called
    /// <see cref="InvalidateAll(string)"/> indiscriminately and surfaced as
    /// <c>"Preview token not found or expired"</c> on every <c>format_range_apply</c>
    /// (and other <c>*_apply</c>) call that raced against a file-watcher auto-reload within
    /// the preview's TTL window. The captured <c>OriginalSolution</c>/<c>ModifiedSolution</c>
    /// snapshots are immutable Roslyn graphs and remain readable after the underlying
    /// <c>MSBuildWorkspace</c> is disposed by reload, so the apply path's existing
    /// cross-lineage rebase (<c>RebaseModifiedSolutionOntoCurrentAsync</c> in
    /// <c>RefactoringService</c>) safely replays the preview's diff onto the post-reload
    /// solution. <see cref="InvalidateAll(string)"/> remains the right call for the
    /// workspace-close path, where the workspace is going away entirely.
    /// </remarks>
    /// <param name="workspaceId">The workspace whose entries are version-checked.</param>
    /// <param name="newWorkspaceVersion">The post-bump workspace version.</param>
    void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion);

    /// <summary>
    /// Returns the workspace identifier associated with a preview token without consuming the entry,
    /// or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    string? PeekWorkspaceId(string token);

    /// <summary>
    /// <b>preview-apply-token-write-path-toctou:</b> returns the file paths of every document the
    /// stored preview adds, changes, or removes — the write set a redeeming <c>*_apply</c> call
    /// will persist to disk — without consuming or TTL-refreshing the entry. Returns
    /// <see langword="null"/> when the token is expired/not found OR when the store cannot
    /// enumerate the write set ("unknown"); callers performing redemption-time boundary
    /// revalidation treat <see langword="null"/> as "skip", never as "empty write set verified".
    /// </summary>
    IReadOnlyList<string>? PeekChangedPaths(string token);

    /// <summary>
    /// <b>preview-token-apply-route-provenance:</b> returns the producer family that created the
    /// stored preview, without consuming or TTL-refreshing the entry. Returns
    /// <see cref="PreviewKind.Unspecified"/> when the token is expired/not found, when the producer
    /// did not record a kind, or when the store does not track provenance — callers treat
    /// <see cref="PreviewKind.Unspecified"/> as "permissive, no provenance claim", never as proof of
    /// a mismatch.
    /// </summary>
    PreviewKind PeekKind(string token);
}
