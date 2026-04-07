using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Computes unified diffs between two Roslyn <see cref="Solution"/> snapshots.
/// </summary>
internal static class SolutionDiffHelper
{
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
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IReadOnlyList<FileChangeDto>> ComputeChangesAsync(
        Solution oldSolution,
        Solution newSolution,
        CancellationToken ct)
    {
        var changes = new List<FileChangeDto>();
        var solutionChanges = newSolution.GetChanges(oldSolution);
        var totalChars = 0;
        var truncatedFileCount = 0;

        bool TryAdd(FileChangeDto change)
        {
            if (totalChars + change.UnifiedDiff.Length > DefaultMaxTotalChars)
            {
                truncatedFileCount++;
                return false;
            }
            changes.Add(change);
            totalChars += change.UnifiedDiff.Length;
            return true;
        }

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = oldSolution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null)
                {
                    continue;
                }

                var oldText = (await oldDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                var newText = (await newDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                if (oldText == newText)
                {
                    continue;
                }

                var filePath = oldDoc.FilePath ?? newDoc.FilePath ?? oldDoc.Name;
                TryAdd(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(oldText, newText, filePath)));
            }

            foreach (var docId in projectChange.GetAddedDocuments())
            {
                var newDoc = newSolution.GetDocument(docId);
                if (newDoc is null)
                {
                    continue;
                }

                var newText = (await newDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                var filePath = newDoc.FilePath ?? newDoc.Name;
                TryAdd(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, newText, filePath)));
            }

            foreach (var docId in projectChange.GetRemovedDocuments())
            {
                var oldDoc = oldSolution.GetDocument(docId);
                if (oldDoc is null)
                {
                    continue;
                }

                var oldText = (await oldDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                var filePath = oldDoc.FilePath ?? oldDoc.Name;
                TryAdd(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(oldText, string.Empty, filePath)));
            }
        }

        if (truncatedFileCount > 0)
        {
            changes.Add(new FileChangeDto(
                "<truncated>",
                $"# FLAG-6A: solution diff exceeded {DefaultMaxTotalChars} characters total. " +
                $"{changes.Count} file diff(s) returned, {truncatedFileCount} omitted. " +
                "Re-run with narrower scope or read affected files directly."));
        }

        return changes;
    }
}
