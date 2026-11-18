using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Materializes one physical C# edit in every project that links the file.</summary>
internal static class InterfaceLinkedDocumentReconciler
{
    internal static ProjectId ResolveGeneratedInterfaceProjectId(Solution baseline, Solution pending, string interfaceName)
    {
        var documents = pending.GetChanges(baseline).GetProjectChanges()
            .SelectMany(change => change.GetAddedDocuments())
            .Select(pending.GetDocument)
            .Where(document => document?.Name == interfaceName + ".cs")
            .ToArray();
        if (documents.Length != 1)
            throw InvalidOperationErrors.Internal($"Generated interface '{interfaceName}' has no unique project identity.");
        return documents[0]!.Project.Id;
    }

    /// <summary>Reconciles every file the edit changed that more than one project compiles.</summary>
    internal static async Task<Solution> ReconcileAsync(
        Solution baseline, Solution pending, ProjectId interfaceProjectId, CancellationToken ct)
    {
        var changedIds = ChangedDocumentIds(baseline, pending);
        foreach (var group in LinkedGroups(pending))
            pending = await ReconcileGroupAsync(baseline, pending, group.Key, group.ToArray(), changedIds, interfaceProjectId, ct).ConfigureAwait(false);

        return pending;
    }

    /// <summary>
    /// Reconciles only the copies of <paramref name="documentId"/>'s file: the route generated its interface beside one
    /// document and edited the other changed documents consistently across their copies itself, so only this document's
    /// final text still has to reach the other projects that compile it.
    /// </summary>
    internal static async Task<Solution> ReconcileLinkedCopiesAsync(
        Solution baseline, Solution pending, DocumentId documentId, ProjectId interfaceProjectId, CancellationToken ct)
    {
        var path = pending.GetDocument(documentId)?.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return pending;
        var changedIds = ChangedDocumentIds(baseline, pending);
        var group = LinkedGroups(pending).FirstOrDefault(candidate => FileSystemPath.Comparer.Equals(candidate.Key, path));
        return group is null
            ? pending
            : await ReconcileGroupAsync(baseline, pending, group.Key, group.ToArray(), changedIds, interfaceProjectId, ct).ConfigureAwait(false);
    }

    private static HashSet<DocumentId> ChangedDocumentIds(Solution baseline, Solution pending) =>
        pending.GetChanges(baseline).GetProjectChanges()
            .SelectMany(change => change.GetChangedDocuments().Concat(change.GetAddedDocuments()))
            .ToHashSet();

    private static IEnumerable<IGrouping<string, Document>> LinkedGroups(Solution pending) =>
        pending.Projects.SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .GroupBy(document => document.FilePath!, FileSystemPath.Comparer);

    private static async Task<Solution> ReconcileGroupAsync(
        Solution baseline, Solution pending, string path, Document[] linked, HashSet<DocumentId> changedIds,
        ProjectId interfaceProjectId, CancellationToken ct)
    {
        if (linked.Length < 2 || !linked.Any(document => changedIds.Contains(document.Id))) return pending;

        var originals = linked.Select(document => baseline.GetDocument(document.Id))
            .Where(document => document is not null).ToArray();
        var originalTexts = await Task.WhenAll(originals.Select(document => document!.GetTextAsync(ct))).ConfigureAwait(false);
        if (originalTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new PublicInvalidOperationException($"Linked C# documents at '{path}' have divergent baseline content.");

        var changed = linked.Where(document => changedIds.Contains(document.Id)).ToArray();
        var changedTexts = await Task.WhenAll(changed.Select(document => document.GetTextAsync(ct))).ConfigureAwait(false);
        if (changedTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new PublicInvalidOperationException($"Linked C# documents at '{path}' have divergent edits.");
        foreach (var document in linked)
        {
            pending = pending.WithDocumentText(document.Id, changedTexts[0]);
            if (document.Project.Id != interfaceProjectId)
                pending = CrossProjectRefactoringService.EnsureProjectReference(pending, document.Project.Id, interfaceProjectId);
        }

        return pending;
    }
}
