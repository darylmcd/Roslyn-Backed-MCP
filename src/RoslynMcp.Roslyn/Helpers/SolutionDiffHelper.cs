using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Computes unified diffs between two Roslyn <see cref="Solution"/> snapshots.
/// </summary>
internal static class SolutionDiffHelper
{
    /// <summary>
    /// Item #4 — sentinel FilePath used on a synthetic FileChangeDto to signal the per-solution
    /// 64 KB cap kicked in. Callers that pipe this list into PreviewStore use the presence of
    /// this sentinel to tag the stored preview as truncated, and the apply path then refuses
    /// to proceed without an explicit <c>force</c> flag (the audit's "preview truncated while
    /// apply still mutates disk" concern).
    /// </summary>
    public const string TruncatedSentinelFilePath = "<truncated>";

    /// <summary>
    /// FLAG-6A: Maximum total characters across all generated FileChangeDtos. When exceeded,
    /// the remaining file diffs are returned as truncation markers and a summary file change
    /// is appended noting how many files were omitted. Protects MCP clients from runaway
    /// preview output budgets.
    /// </summary>
    public const int DefaultMaxTotalChars = 64 * 1024;

    /// <summary>
    /// Returns a <see cref="FileChangeDto"/> for each document that differs between
    /// <paramref name="oldSolution"/> and <paramref name="newSolution"/>,
    /// including added and removed documents.
    /// </summary>
    /// <param name="oldSolution">The baseline solution snapshot.</param>
    /// <param name="newSolution">The modified solution snapshot to compare against.</param>
    /// <param name="readContext">Required physical-read authorization or explicit materialized-snapshot-only policy.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IReadOnlyList<FileChangeDto>> ComputeChangesAsync(
        Solution oldSolution,
        Solution newSolution,
        SolutionDiffReadContext readContext,
        CancellationToken ct)
    {
        var mutations = await CreateMutationPlanAsync(oldSolution, newSolution, readContext,
            allowVirtualPaths: true, ct).ConfigureAwait(false);
        return ComputeChanges(mutations);
    }

    internal static async Task<IReadOnlyList<SolutionFileMutationPlanner.Mutation>> CreateMutationPlanAsync(
        Solution oldSolution, Solution newSolution, SolutionDiffReadContext readContext,
        bool allowVirtualPaths, CancellationToken ct)
    {
        (oldSolution, newSolution) = await readContext.PrepareAsync(oldSolution, newSolution, ct).ConfigureAwait(false);
        var owners = newSolution.GetChanges(oldSolution).GetProjectChanges()
            .Where(SolutionFileMutationPlanner.RequiresPhysicalProjectRender).Select(change => change.OldProject.Id).ToArray();
        var referenceAuthority = owners.Length == 0 ? CompilerReferenceAuthority.ForNonRenderingDelta(oldSolution, newSolution)
            : await RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(oldSolution, owners,
                readContext.ReadBytesAsync, readContext.ReadDigestAsync, readContext.AuthorizePathsAsync, ct).ConfigureAwait(false);
        return await SolutionFileMutationPlanner.CreateAsync(oldSolution, newSolution,
            readContext.ReadBytesAsync, referenceAuthority, includeDocuments: true, allowVirtualPaths, ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<FileChangeDto> ComputeChanges(IReadOnlyList<SolutionFileMutationPlanner.Mutation> mutations)
    {
        var changes = new List<FileChangeDto>();
        var totalChars = 0;
        var truncatedFiles = new List<string>();

        bool TryAdd(FileChangeDto change)
        {
            // format-document-preview-empty-diff-instead-of-noop (gh #739): belt-and-suspenders
            // guard — if DiffGenerator returned string.Empty (no net changes after a no-op
            // formatter pass, despite distinct SourceText instances that bypassed the
            // oldText == newText guard above), skip the entry so callers see an empty
            // changes[] rather than a header-only diff with no hunks.
            if (string.IsNullOrEmpty(change.UnifiedDiff))
            {
                return false;
            }
            if (totalChars + change.UnifiedDiff.Length > DefaultMaxTotalChars)
            {
                truncatedFiles.Add(change.FilePath);
                return false;
            }
            changes.Add(change);
            totalChars += change.UnifiedDiff.Length;
            return true;
        }

        foreach (var mutation in mutations)
        {
            TryAdd(new FileChangeDto(mutation.Path,
                DiffGenerator.GenerateUnifiedDiff(mutation.OldText, mutation.NewText, mutation.Path)));
        }

        if (truncatedFiles.Count > 0)
        {
            // move-file-preview-large-diff-truncation: surface the list of affected file paths
            // prominently so the caller can re-read them individually (or rerun with a narrower
            // scope) without guessing which files were dropped. NetworkDocumentation audit §15.
            var fileList = string.Join(", ", truncatedFiles.Take(10));
            if (truncatedFiles.Count > 10)
            {
                fileList += $", … ({truncatedFiles.Count - 10} more)";
            }
            changes.Add(new FileChangeDto(
                TruncatedSentinelFilePath,
                $"# FLAG-6A: solution diff exceeded {DefaultMaxTotalChars} characters total. " +
                $"{changes.Count} file diff(s) returned, {truncatedFiles.Count} omitted. " +
                $"Omitted file(s): {fileList}. " +
                "Re-run with narrower scope or read the listed files directly with the Read tool."));
        }

        return changes;
    }
}
