using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Computes unified diffs between two Roslyn <see cref="Solution"/> snapshots.
/// </summary>
internal static class SolutionDiffHelper
{
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
                changes.Add(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(oldText, newText, filePath)));
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
                changes.Add(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, newText, filePath)));
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
                changes.Add(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(oldText, string.Empty, filePath)));
            }
        }

        return changes;
    }
}