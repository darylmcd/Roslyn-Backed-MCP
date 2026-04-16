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
    /// Stores a modified solution snapshot and returns an opaque preview token.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier the solution belongs to.</param>
    /// <param name="modifiedSolution">The modified Roslyn solution to store.</param>
    /// <param name="workspaceVersion">The workspace version at the time preview was computed.</param>
    /// <param name="description">A human-readable description of the pending operation.</param>
    /// <returns>An opaque token that can be passed to <see cref="Retrieve"/> or <see cref="Invalidate"/>.</returns>
    string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description);

    /// <summary>
    /// Item #4 — stores a preview along with a flag indicating whether the preview's diff
    /// was truncated by the per-solution / per-file caps in <see cref="Helpers.SolutionDiffHelper"/>.
    /// The apply path refuses to redeem a truncated preview unless the caller opts into a
    /// blind apply via <c>force: true</c>.
    /// </summary>
    string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated);

    /// <summary>
    /// Item #4 — convenience overload: derives the truncated flag from the presence of
    /// <see cref="Helpers.SolutionDiffHelper.TruncatedSentinelFilePath"/> in <paramref name="changes"/>.
    /// </summary>
    string Store(
        string workspaceId,
        Solution modifiedSolution,
        int workspaceVersion,
        string description,
        IReadOnlyList<FileChangeDto> changes);

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
    /// Returns the workspace identifier associated with a preview token without consuming the entry,
    /// or <see langword="null"/> if the token is expired or not found.
    /// </summary>
    string? PeekWorkspaceId(string token);
}
