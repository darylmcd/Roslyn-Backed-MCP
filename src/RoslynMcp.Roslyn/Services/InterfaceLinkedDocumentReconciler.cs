using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Materializes one physical C# edit in every project that links the file.</summary>
internal static class InterfaceLinkedDocumentReconciler
{
    /// <summary>What a caller of the same-project route can change when the copies of a file were edited differently.</summary>
    internal const string StandaloneRemedy =
        "Preview with replaceUsages=false, or choose a different interface name, before previewing again.";

    /// <summary>What a caller of a cross-project route can change (those routes have no replaceUsages).</summary>
    internal const string CrossProjectRemedy =
        "Choose a different interface name, or unlink the file from one of the projects, before previewing again.";

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

    /// <summary>The documents of <paramref name="pending"/> whose text differs from <paramref name="since"/>.</summary>
    internal static List<DocumentId> DocumentsChangedSince(Solution since, Solution pending) =>
        pending.GetChanges(since).GetProjectChanges().SelectMany(change => change.GetChangedDocuments()).ToList();

    /// <summary>
    /// Reconciles only the files of <paramref name="documentIds"/>: a route names the documents it edited one copy of (the
    /// source type's base list, the documents a usage or constructor rewrite changed). A document the re-pin edited in every
    /// linked copy is already consistent, and reconciling it would only add a project reference its text does not need.
    /// <paramref name="remedy"/> is what the caller can change when the copies were edited differently.
    /// </summary>
    internal static async Task<Solution> ReconcileLinkedCopiesAsync(
        Solution baseline, Solution pending, IReadOnlyCollection<DocumentId> documentIds, ProjectId interfaceProjectId,
        string remedy, CancellationToken ct)
    {
        var paths = documentIds.Select(id => pending.GetDocument(id)?.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(FileSystemPath.Comparer);
        var changedIds = pending.GetChanges(baseline).GetProjectChanges()
            .SelectMany(change => change.GetChangedDocuments().Concat(change.GetAddedDocuments()))
            .ToHashSet();
        var groups = pending.Projects.SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .GroupBy(document => document.FilePath!, FileSystemPath.Comparer)
            .Where(group => paths.Contains(group.Key));
        foreach (var group in groups)
            pending = await ReconcileGroupAsync(baseline, pending, group.Key, group.ToArray(), changedIds, interfaceProjectId, remedy, ct).ConfigureAwait(false);

        return pending;
    }

    private static async Task<Solution> ReconcileGroupAsync(
        Solution baseline, Solution pending, string path, Document[] linked, HashSet<DocumentId> changedIds,
        ProjectId interfaceProjectId, string remedy, CancellationToken ct)
    {
        if (linked.Length < 2 || !linked.Any(document => changedIds.Contains(document.Id))) return pending;

        var originals = linked.Select(document => baseline.GetDocument(document.Id))
            .Where(document => document is not null).ToArray();
        var originalTexts = await Task.WhenAll(originals.Select(document => document!.GetTextAsync(ct))).ConfigureAwait(false);
        if (originalTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            throw new PublicInvalidOperationException(
                $"Linked C# documents at '{path}' have different content in different projects, so the preview cannot write one final " +
                $"text to the shared file; reload the workspace and retry. {remedy}");
        }

        var changed = linked.Where(document => changedIds.Contains(document.Id)).ToArray();
        var changedTexts = await Task.WhenAll(changed.Select(document => document.GetTextAsync(ct))).ConfigureAwait(false);
        if (changedTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            throw new PublicInvalidOperationException(
                $"Linked C# documents at '{path}' were changed in different ways in different projects, so one final text cannot be " +
                $"written to the shared file. {remedy}");
        }

        foreach (var document in linked)
        {
            pending = pending.WithDocumentText(document.Id, changedTexts[0]);
            if (document.Project.Id != interfaceProjectId)
                pending = CrossProjectRefactoringService.EnsureProjectReference(pending, document.Project.Id, interfaceProjectId);
        }

        return pending;
    }
}
