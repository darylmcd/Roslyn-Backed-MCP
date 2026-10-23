using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Filters Roslyn's case-insensitive path index using the host filesystem identity.</summary>
internal static class DocumentPathLookup
{
    internal static ImmutableArray<DocumentId> GetDocumentIds(Solution solution, string filePath) =>
        solution.GetDocumentIdsWithFilePath(filePath)
            .Where(id => solution.GetDocument(id) is { FilePath: { } candidate }
                && FileSystemPath.Comparer.Equals(candidate, filePath))
            .ToImmutableArray();
}
