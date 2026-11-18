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
    /// Reconciles only the files of <paramref name="documentIds"/>: a route that edits some documents consistently across
    /// their linked copies itself (the re-pin) names just the documents it edited one copy of (the source type, the
    /// documents a usage rewrite changed), so unrelated linked files do not gain a project reference they do not need.
    /// </summary>
    internal static async Task<Solution> ReconcileLinkedCopiesAsync(
        Solution baseline, Solution pending, IReadOnlyCollection<DocumentId> documentIds, ProjectId interfaceProjectId, CancellationToken ct)
    {
        var paths = documentIds.Select(id => pending.GetDocument(id)?.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(FileSystemPath.Comparer);
        var changedIds = ChangedDocumentIds(baseline, pending);
        foreach (var group in LinkedGroups(pending).Where(candidate => paths.Contains(candidate.Key)))
            pending = await ReconcileGroupAsync(baseline, pending, group.Key, group.ToArray(), changedIds, interfaceProjectId, ct).ConfigureAwait(false);

        return pending;
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
            throw new PublicInvalidOperationException($"Linked C# documents at '{path}' have different content in different projects, so the preview cannot write one final text to the shared file; reload the workspace and retry.");

        var changed = linked.Where(document => changedIds.Contains(document.Id)).ToArray();
        var changedTexts = await Task.WhenAll(changed.Select(document => document.GetTextAsync(ct))).ConfigureAwait(false);
        if (changedTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new PublicInvalidOperationException($"Linked C# documents at '{path}' were changed in different ways in different projects, so one final text cannot be written to the shared file; " +
                "preview with replaceUsages=false, or choose a different interface name, before previewing again.");
        foreach (var document in linked)
        {
            pending = pending.WithDocumentText(document.Id, changedTexts[0]);
            if (document.Project.Id != interfaceProjectId)
                pending = CrossProjectRefactoringService.EnsureProjectReference(pending, document.Project.Id, interfaceProjectId);
        }

        return pending;
    }
}
