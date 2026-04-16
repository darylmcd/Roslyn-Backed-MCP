using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Thread-safe, TTL-bounded in-memory store for pending Roslyn <see cref="Solution"/> previews.
/// Entries expire after a configurable TTL (default 5 minutes). The store is capped at
/// <paramref name="maxEntries"/> entries; oldest entries are evicted when the limit is reached.
/// </summary>
/// <remarks>
/// <para>
/// <b>preview-token-cross-coupling-bundle (BREAKING):</b> each entry now captures an isolated
/// per-token snapshot pair — the <c>OriginalSolution</c> at the moment the preview was stored
/// AND the <c>ModifiedSolution</c> (with the preview's edits applied). The apply path computes
/// the preview's intended diff as <c>ModifiedSolution.GetChanges(OriginalSolution)</c> and
/// replays only THAT diff onto the current workspace solution. Sibling <c>*_apply</c> calls
/// against unrelated files no longer invalidate this entry: the workspace lifecycle (Close,
/// Reload) is the only event that drops tokens via <see cref="InvalidateAll"/>. The workspace
/// version tracked on the entry is retained for telemetry and diagnostics only — it is no
/// longer used to reject an apply.
/// </para>
/// </remarks>
public sealed class PreviewStore : BoundedStore<PreviewStore.PreviewEntry>, IPreviewStore
{
    public PreviewStore(int maxEntries = 20, TimeSpan? ttl = null)
        : base(maxEntries, ttl ?? TimeSpan.FromMinutes(5)) { }

    /// <summary>
    /// Legacy overload — assumes the preview's diff was not truncated. Kept so unrelated
    /// callers (e.g. tests) don't need to adjust. New production callers should use the
    /// overload that accepts <paramref name="diffTruncated"/> (Item #4).
    /// </summary>
    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
        => Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated: false);

    /// <summary>
    /// Item #4 — stores a preview along with a flag indicating whether the preview's
    /// on-the-wire diff was truncated by <see cref="SolutionDiffHelper.DefaultMaxTotalChars"/>
    /// or per-file truncation. The apply path refuses to redeem a token marked
    /// <paramref name="diffTruncated"/>=true unless the caller explicitly opts in to a
    /// blind apply via <c>force: true</c>.
    /// </summary>
    /// <remarks>
    /// preview-token-cross-coupling-bundle: the captured <c>OriginalSolution</c> snapshot is
    /// derived from <paramref name="modifiedSolution"/>'s <see cref="Solution.Workspace"/>
    /// current solution at Store time. Callers that already have an explicit original
    /// solution should prefer the overload that takes it directly, but the derived path
    /// is correct for all in-tree call sites: every preview service produces
    /// <paramref name="modifiedSolution"/> by mutating the workspace's <c>CurrentSolution</c>,
    /// so <c>Solution.Workspace.CurrentSolution</c> at Store time IS the original.
    /// </remarks>
    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
        => Store(workspaceId, modifiedSolution.Workspace.CurrentSolution, modifiedSolution, workspaceVersion, description, diffTruncated);

    /// <summary>
    /// preview-token-cross-coupling-bundle: explicit-original-solution overload. Use this
    /// when the caller needs to record a different base snapshot than the workspace's
    /// current solution at Store time.
    /// </summary>
    public string Store(
        string workspaceId,
        Solution originalSolution,
        Solution modifiedSolution,
        int workspaceVersion,
        string description,
        bool diffTruncated)
        => StoreEntry(new PreviewEntry(workspaceId, originalSolution, modifiedSolution, workspaceVersion, description, diffTruncated, DateTime.UtcNow));

    /// <summary>
    /// Convenience: when the caller has a freshly-computed <see cref="FileChangeDto"/> list
    /// from <see cref="SolutionDiffHelper.ComputeChangesAsync"/>, derive the truncated flag
    /// from the sentinel entry so the caller doesn't have to remember to inspect it.
    /// </summary>
    public string Store(
        string workspaceId,
        Solution modifiedSolution,
        int workspaceVersion,
        string description,
        IReadOnlyList<FileChangeDto> changes)
        => Store(
            workspaceId,
            modifiedSolution,
            workspaceVersion,
            description,
            diffTruncated: ContainsTruncatedSentinel(changes));

    private static bool ContainsTruncatedSentinel(IReadOnlyList<FileChangeDto> changes)
    {
        foreach (var change in changes)
        {
            if (string.Equals(change.FilePath, SolutionDiffHelper.TruncatedSentinelFilePath, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// preview-token-cross-coupling-bundle (BREAKING): the returned tuple now exposes
    /// <c>OriginalSolution</c> — the workspace snapshot at the moment the preview was
    /// stored — so the apply path can compute the preview's intended diff via
    /// <c>ModifiedSolution.GetChanges(OriginalSolution)</c> and rebase it onto the current
    /// workspace solution. Callers should NOT pass <c>ModifiedSolution</c> to
    /// <c>Workspace.TryApplyChanges</c> directly: that would either fail on a lineage
    /// mismatch or silently undo unrelated sibling edits (documents present in the current
    /// workspace but not in <c>ModifiedSolution</c> would be reported as removed).
    /// </summary>
    public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
    {
        var entry = RetrieveEntry(token);
        if (entry is null) return null;
        return (entry.WorkspaceId, entry.OriginalSolution, entry.ModifiedSolution, entry.WorkspaceVersion, entry.Description, entry.DiffTruncated);
    }

    public sealed record PreviewEntry(
        string WorkspaceId,
        Solution OriginalSolution,
        Solution ModifiedSolution,
        int WorkspaceVersion,
        string Description,
        bool DiffTruncated,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
