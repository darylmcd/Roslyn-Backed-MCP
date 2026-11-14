using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class CompositeSolutionMutationPlanner
{
    internal sealed record FileEdit(CompositeFileMutation Mutation, string? OriginalContent);
    private static readonly UTF8Encoding _strictUnicode = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static async Task<List<FileEdit>> BuildAsync(
        Solution baseline, Solution pending, IReadOnlyCollection<DocumentId> requiredCreates, CancellationToken ct)
    {
        var affectedPaths = GetTouchedPaths(baseline, pending).DistinctBy(Path.GetFullPath, FileSystemPath.Comparer).ToArray();
        if (affectedPaths.Length == 0)
            return [];

        var beforeByPath = IndexDocuments(baseline);
        var afterByPath = IndexDocuments(pending);
        var edits = new List<FileEdit>(affectedPaths.Length);
        foreach (var rawPath in affectedPaths)
        {
            ct.ThrowIfCancellationRequested();
            var path = ResolvePhysicalReviewPath(rawPath);
            var originals = beforeByPath[path];
            var surviving = afterByPath[path];
            foreach (var owner in originals.Concat(surviving))
                _ = ResolvePhysicalReviewPath(RequirePath(owner));
            if (surviving.Any(document => requiredCreates.Contains(document.Id)) &&
                (beforeByPath.Contains(path) || BaselineUsesPhysicalPath(beforeByPath, path, ct)))
            {
                throw new PublicInvalidOperationException(
                    "A generated file's destination is already mapped by the workspace. Choose a different destination and re-issue the preview.");
            }

            var originalContent = await ReadAgreedContentAsync(originals, ct).ConfigureAwait(false);
            if (!afterByPath.Contains(path))
            {
                // Removing a link only deletes disk content when its last owner is removed.
                edits.Add(new FileEdit(new CompositeFileMutation(path, null, CompositeFileMutationMode.Delete), originalContent));
                continue;
            }

            if (originalContent is null)
            {
                CompositeCreateDestination.RequireAvailableForPreview(path);
                var createdContent = await ReadAgreedContentAsync(surviving, ct).ConfigureAwait(false);
                edits.Add(new FileEdit(new CompositeFileMutation(path, createdContent, CompositeFileMutationMode.Create), null));
                continue;
            }

            var editedContent = await ResolveExistingEditAsync(originals, surviving, originalContent, ct).ConfigureAwait(false);
            if (editedContent is not null)
                edits.Add(new FileEdit(new CompositeFileMutation(path, editedContent, CompositeFileMutationMode.Edit), originalContent));
        }

        return edits;
    }

    internal static IEnumerable<string> GetTouchedPaths(Solution baseline, Solution pending)
    {
        foreach (var change in pending.GetChanges(baseline).GetProjectChanges())
        {
            foreach (var id in change.GetChangedDocuments())
            {
                var before = RequireReviewPath(RequireDocument(baseline, id));
                var after = RequireReviewPath(RequireDocument(pending, id));
                if (!FileSystemPath.Comparer.Equals(before, after)) yield return before;
                yield return after;
            }
            foreach (var id in change.GetAddedDocuments())
                yield return RequireReviewPath(RequireDocument(pending, id));
            foreach (var id in change.GetRemovedDocuments())
                yield return RequireReviewPath(RequireDocument(baseline, id));
        }
    }

    internal static async Task<Solution> PropagateLinkedEditsAsync(
        Solution baseline, Solution pending, IReadOnlyCollection<string> touchedPaths, CancellationToken ct)
    {
        if (touchedPaths.Count == 0) return pending;

        var afterByPath = IndexDocuments(pending);
        var beforeByPath = IndexDocuments(baseline);
        foreach (var path in touchedPaths)
        {
            var surviving = afterByPath[path].ToArray();
            if (surviving.Length < 2) continue;

            var originals = beforeByPath[path];
            var originalContent = await ReadAgreedContentAsync(originals, ct).ConfigureAwait(false);
            if (originalContent is null) continue;

            var editedContent = await ResolveExistingEditAsync(originals, surviving, originalContent, ct).ConfigureAwait(false);
            if (editedContent is null) continue;

            foreach (var owner in surviving)
            {
                var text = await owner.GetTextAsync(ct).ConfigureAwait(false);
                if (text.ToString() != editedContent)
                    pending = pending.WithDocumentText(owner.Id, SourceText.From(editedContent, text.Encoding, text.ChecksumAlgorithm));
            }
        }

        return pending;
    }

    internal static string ResolvePhysicalReviewPath(string path)
    {
        RequireReviewPath(path);
        try
        {
            // Use the canonical spelling only after verifying that the raw path resolves to it.
            if (CompositeCreateDestination.IsPhysicalPath(path)) return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PublicInvalidOperationException("The composite mutation's physical file path could not be verified.", ex);
        }
        throw new PublicInvalidOperationException("A composite mutation requires a physical file path without filesystem links.");
    }

    private static string RequireReviewPath(Document document) => RequireReviewPath(RequirePath(document));

    private static string RequireReviewPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) ||
            path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(segment => segment is "." or ".."))
            throw new PublicInvalidOperationException("A composite mutation requires an absolute file path without traversal or control characters.");
        return path;
    }

    internal static IReadOnlyList<FileChangeDto> RenderReview(IReadOnlyList<FileEdit> edits, CancellationToken ct)
    {
        var changes = new List<FileChangeDto>(edits.Count);
        var totalChars = 0;
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            var mutation = edit.Mutation;
            RequireUnicode(mutation.FilePath);
            RequireUnicode(edit.OriginalContent);
            RequireUnicode(mutation.UpdatedContent);
            var (diff, truncated) = DiffGenerator.RenderUnifiedDiff(
                edit.OriginalContent, mutation.UpdatedContent, mutation.FilePath);
            if (diff.Length == 0)
                throw InvalidOperationErrors.Internal("A planned composite mutation must have a visible diff.");
            totalChars += diff.Length;
            if (truncated || diff.Length > DiffGenerator.DefaultMaxDiffChars || totalChars > SolutionDiffHelper.DefaultMaxTotalChars)
                throw new PublicInvalidOperationException(
                    "The composite preview exceeds the diff review budget. Split the operation and reissue the preview.");

            changes.Add(new FileChangeDto(mutation.FilePath, diff));
        }

        return changes;
    }

    private static void RequireUnicode(string? text)
    {
        if (text is null) return;
        try { _ = _strictUnicode.GetByteCount(text); }
        catch (EncoderFallbackException ex)
        {
            throw new PublicInvalidOperationException("A composite mutation requires valid Unicode text. Correct the edit and reissue the preview.", ex);
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
            .ToLookup(GetPath, FileSystemPath.Comparer);

    private static async Task<string?> ReadAgreedContentAsync(IEnumerable<Document> owners, CancellationToken ct)
    {
        string? agreed = null;
        foreach (var owner in owners)
        {
            var content = (await owner.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (agreed is not null && agreed != content)
                throw new PublicInvalidOperationException("Linked C# documents have divergent composite mutations.");
            agreed = content;
        }

        return agreed;
    }

    private static async Task<string?> ResolveExistingEditAsync(
        IEnumerable<Document> originals, IEnumerable<Document> surviving, string originalContent, CancellationToken ct)
    {
        var originalIds = originals.Select(document => document.Id).ToHashSet();
        string? editedContent = null;
        var newOwners = new List<Document>();
        foreach (var owner in surviving)
        {
            if (!originalIds.Contains(owner.Id))
            {
                newOwners.Add(owner);
                continue;
            }

            var content = (await owner.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            // An unchanged linked view has no independent write intent. The changed view's edit will publish to both.
            if (content == originalContent) continue;
            if (editedContent is not null && editedContent != content)
                throw new PublicInvalidOperationException("Linked C# documents have divergent composite mutations.");
            editedContent = content;
        }

        var addedContent = await ReadAgreedContentAsync(newOwners, ct).ConfigureAwait(false);
        if (addedContent is not null && addedContent != (editedContent ?? originalContent))
            throw new PublicInvalidOperationException("A new linked C# document cannot replace the existing content at its destination.");

        return editedContent;
    }

    private static Document RequireDocument(Solution solution, DocumentId id) =>
        solution.GetDocument(id) ?? throw InvalidOperationErrors.Internal("A composite C# document could not be resolved.");

    private static string GetPath(Document document) => Path.GetFullPath(RequirePath(document));

    private static string RequirePath(Document document) =>
        document.FilePath is { } path && Path.IsPathFullyQualified(path)
            ? path
            : throw new PublicInvalidOperationException("A changed C# document is not mapped to an absolute physical file path.");
}
