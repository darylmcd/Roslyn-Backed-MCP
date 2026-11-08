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

    internal static async Task<Solution> ReconcileAsync(
        Solution baseline, Solution pending, ProjectId interfaceProjectId, CancellationToken ct)
    {
        var changedIds = pending.GetChanges(baseline).GetProjectChanges()
            .SelectMany(change => change.GetChangedDocuments().Concat(change.GetAddedDocuments()))
            .ToHashSet();
        var groups = pending.Projects.SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .GroupBy(document => document.FilePath!, FileSystemPath.Comparer);
        foreach (var group in groups)
        {
            var linked = group.ToArray();
            if (linked.Length < 2 || !linked.Any(document => changedIds.Contains(document.Id))) continue;

            var originals = linked.Select(document => baseline.GetDocument(document.Id))
                .Where(document => document is not null).ToArray();
            var originalTexts = await Task.WhenAll(originals.Select(document => document!.GetTextAsync(ct))).ConfigureAwait(false);
            if (originalTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new PublicInvalidOperationException($"Linked C# documents at '{group.Key}' have divergent baseline content.");

            var changed = linked.Where(document => changedIds.Contains(document.Id)).ToArray();
            var changedTexts = await Task.WhenAll(changed.Select(document => document.GetTextAsync(ct))).ConfigureAwait(false);
            if (changedTexts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new PublicInvalidOperationException($"Linked C# documents at '{group.Key}' have divergent edits.");
            foreach (var document in linked)
            {
                pending = pending.WithDocumentText(document.Id, changedTexts[0]);
                if (document.Project.Id != interfaceProjectId)
                    pending = CrossProjectRefactoringService.EnsureProjectReference(pending, document.Project.Id, interfaceProjectId);
            }
        }

        return pending;
    }
}
