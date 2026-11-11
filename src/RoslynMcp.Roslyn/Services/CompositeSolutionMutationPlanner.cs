using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class CompositeSolutionMutationPlanner
{
    internal static async Task<List<CompositeFileMutation>> BuildAsync(
        Solution baseline, Solution pending, IReadOnlyCollection<DocumentId> requiredCreates, CancellationToken ct)
    {
        var affectedPaths = new Dictionary<string, (string RawPath, List<(Document Document, bool Added)> Writes)>(FileSystemPath.Comparer);
        foreach (var change in pending.GetChanges(baseline).GetProjectChanges())
        {
            foreach (var id in change.GetChangedDocuments())
                AddWrite(RequireDocument(pending, id), added: false);
            foreach (var id in change.GetAddedDocuments())
                AddWrite(RequireDocument(pending, id), added: true);
            foreach (var id in change.GetRemovedDocuments())
                _ = GetWrites(RequireDocument(baseline, id));
        }

        if (affectedPaths.Count == 0)
            return [];

        var beforeByPath = IndexDocuments(baseline);
        var afterByPath = IndexDocuments(pending);
        var mutations = new List<CompositeFileMutation>(affectedPaths.Count);
        foreach (var (path, (rawPath, writes)) in affectedPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (writes.Count == 0)
            {
                // Removing a link is a project change. Delete the file only once its last link is gone.
                if (!afterByPath.Contains(path))
                    mutations.Add(new CompositeFileMutation(rawPath, null, CompositeFileMutationMode.Delete));
                continue;
            }

            var originalPath = RequirePath(writes[0].Document);
            var mode = beforeByPath.Contains(path) ? CompositeFileMutationMode.Edit : CompositeFileMutationMode.Create;
            if (writes.Any(write => requiredCreates.Contains(write.Document.Id)) &&
                (mode != CompositeFileMutationMode.Create || BaselineUsesPhysicalPath(beforeByPath, path, ct)))
            {
                throw new PublicInvalidOperationException(
                    "A generated file's destination is already mapped by the workspace. Choose a different destination and re-issue the preview.");
            }

            string? newContent = null;
            foreach (var (document, _) in writes)
            {
                if (mode == CompositeFileMutationMode.Create)
                    CompositeCreateDestination.RequireAvailableForPreview(RequirePath(document));

                var content = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                if (newContent is not null && !string.Equals(newContent, content, StringComparison.Ordinal))
                    throw new PublicInvalidOperationException("Linked C# documents have divergent composite mutations.");
                newContent = content;
            }

            if (mode == CompositeFileMutationMode.Edit && writes.All(write => write.Added))
            {
                if (await BaselineMatchesAsync(beforeByPath[path], newContent, ct).ConfigureAwait(false))
                    continue; // A link to the original content requires no disk rewrite.

                throw new PublicInvalidOperationException("A new linked C# document cannot replace the existing content at its destination.");
            }

            mutations.Add(new CompositeFileMutation(originalPath, newContent, mode));
        }

        return mutations;

        void AddWrite(Document document, bool added) => GetWrites(document).Add((document, added));

        List<(Document Document, bool Added)> GetWrites(Document document)
        {
            var rawPath = RequirePath(document);
            var path = Path.GetFullPath(rawPath);
            if (!affectedPaths.TryGetValue(path, out var affected))
            {
                affected = (rawPath, []);
                affectedPaths.Add(path, affected);
            }

            return affected.Writes;
        }
    }

    private static bool BaselineUsesPhysicalPath(ILookup<string, Document> originals, string path, CancellationToken ct)
    {
        try
        {
            foreach (var group in originals)
            {
                ct.ThrowIfCancellationRequested();
                if (group.Any(document => FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(RequirePath(document)), path)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PublicInvalidOperationException(
                "The composite preview could not verify the destination against physical workspace paths.", ex);
        }

        return false;
    }

    private static ILookup<string, Document> IndexDocuments(Solution solution) =>
        solution.Projects.SelectMany(project => project.Documents)
            .Where(document => Path.IsPathFullyQualified(document.FilePath ?? string.Empty))
            .ToLookup(document => Path.GetFullPath(RequirePath(document)), FileSystemPath.Comparer);

    private static async Task<bool> BaselineMatchesAsync(
        IEnumerable<Document> originals, string? candidate, CancellationToken ct)
    {
        foreach (var original in originals)
        {
            var content = (await original.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (!string.Equals(content, candidate, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static Document RequireDocument(Solution solution, DocumentId id) =>
        solution.GetDocument(id) ?? throw InvalidOperationErrors.Internal("A composite C# document could not be resolved.");

    private static string RequirePath(Document document) =>
        document.FilePath is { } path && Path.IsPathFullyQualified(path)
            ? path
            : throw new PublicInvalidOperationException("A changed C# document is not mapped to an absolute physical file path.");
}
