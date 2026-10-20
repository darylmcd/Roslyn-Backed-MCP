using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Helpers;

internal static class CallsiteUpdateProjection
{
    /// <summary>
    /// Projects per-document callsite counts into one <see cref="CallsiteUpdateDto"/> per physical
    /// file. Counting is keyed by <see cref="DocumentId"/> so case-distinct files stay distinct on
    /// case-sensitive filesystems; documents sharing one physical path (a file linked into several
    /// projects, or a multi-targeted project) are aggregated using the platform path identity.
    /// </summary>
    public static List<CallsiteUpdateDto> Project(
        Solution solution,
        IReadOnlyDictionary<DocumentId, int> countsByDocument) =>
        countsByDocument
            .Select(pair =>
            {
                var document = solution.GetDocument(pair.Key)!;
                return (Path: document.FilePath ?? document.Name, Count: pair.Value);
            })
            .GroupBy(entry => entry.Path, FileSystemPath.Comparer)
            .Select(group => new CallsiteUpdateDto(group.First().Path, group.Sum(entry => entry.Count)))
            .OrderBy(update => update.FilePath, StringComparer.Ordinal)
            .ToList();
}
