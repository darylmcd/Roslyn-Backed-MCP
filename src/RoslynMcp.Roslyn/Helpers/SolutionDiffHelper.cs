using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
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
    /// Returns one <see cref="FileChangeDto"/> per changed physical path (or per DocumentId
    /// when no path is mapped) between
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
        var candidates = new List<(string FilePath, string Identity, string Before, string After)>();
        var solutionChanges = newSolution.GetChanges(oldSolution);

        void AddCandidate(string filePath, string identity, string before, string after) =>
            candidates.Add((filePath, identity, before, after));

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
                AddCandidate(filePath, oldDoc.FilePath ?? newDoc.FilePath ?? "\0document:" + docId, oldText, newText);
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
                AddCandidate(filePath, newDoc.FilePath ?? "\0document:" + docId, string.Empty, newText);
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
                AddCandidate(filePath, oldDoc.FilePath ?? "\0document:" + docId, oldText, string.Empty);
            }
        }

        foreach (var mutation in await ProjectReferenceMutationRenderer.PlanAsync(oldSolution, newSolution, ct).ConfigureAwait(false))
        {
            var before = CsprojSemanticEquality.CreateSnapshot(mutation.Original).Content;
            var after = CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content;
            AddCandidate(mutation.Path, mutation.Path, before, after);
        }

        // A linked physical file may have several DocumentIds. Reconcile its diffs before charging the
        // output budget, or duplicate views can manufacture a truncated preview and a force-only apply.
        var changes = new List<FileChangeDto>();
        var totalChars = 0;
        var truncatedFiles = new List<string>();
        foreach (var group in candidates.GroupBy(candidate => candidate.Identity, FileSystemPath.Comparer))
        {
            var distinctEdits = group.Select(candidate => (candidate.Before, candidate.After))
                .Distinct().Take(2).ToArray();
            if (distinctEdits.Length > 1)
                throw new PublicInvalidOperationException($"Linked C# documents at '{Path.GetFileName(group.First().FilePath)}' have divergent preview diffs.");
            var candidate = group.First();
            var diff = DiffGenerator.GenerateUnifiedDiff(candidate.Before, candidate.After, candidate.FilePath);
            // format-document-preview-empty-diff-instead-of-noop (gh #739): a formatter can hand
            // us distinct SourceText instances whose final strings are equal.
            if (string.IsNullOrEmpty(diff)) continue;
            var change = new FileChangeDto(candidate.FilePath, diff);
            if (totalChars + change.UnifiedDiff.Length > DefaultMaxTotalChars)
            {
                truncatedFiles.Add(change.FilePath);
                continue;
            }
            changes.Add(change);
            totalChars += change.UnifiedDiff.Length;
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
