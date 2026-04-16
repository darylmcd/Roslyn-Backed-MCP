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
    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
        => StoreEntry(new PreviewEntry(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated, DateTime.UtcNow));

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

    public (string WorkspaceId, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
    {
        var entry = RetrieveEntry(token);
        if (entry is null) return null;
        return (entry.WorkspaceId, entry.ModifiedSolution, entry.WorkspaceVersion, entry.Description, entry.DiffTruncated);
    }

    public sealed record PreviewEntry(
        string WorkspaceId,
        Solution ModifiedSolution,
        int WorkspaceVersion,
        string Description,
        bool DiffTruncated,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
