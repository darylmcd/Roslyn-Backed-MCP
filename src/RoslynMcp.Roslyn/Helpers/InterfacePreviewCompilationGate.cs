using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Rejects new compiler errors in the final solution of an interface preview.</summary>
internal static class InterfacePreviewCompilationGate
{
    public static async Task ValidateAsync(Solution baseline, Solution pending, CancellationToken ct)
    {
        var changed = pending.GetChanges(baseline).GetProjectChanges()
            .Select(change => change.NewProject.Id).ToHashSet();
        var affected = changed.ToHashSet();
        var graph = pending.GetProjectDependencyGraph();
        foreach (var id in changed)
            affected.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(id));

        var introduced = new List<(string Project, Diagnostic Diagnostic)>();
        foreach (var id in affected)
        {
            var after = pending.GetProject(id);
            if (after is null) continue;
            var before = baseline.GetProject(id);
            Diagnostic[] beforeErrors = before is null ? [] : (await before.GetCompilationAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Baseline compilation for '{before.Name}' is unavailable."))
                .GetDiagnostics(ct).ToArray();
            var afterErrors = (await after.GetCompilationAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Pending compilation for '{after.Name}' is unavailable."))
                .GetDiagnostics(ct);
            string LocationKey(Location location, bool inPending)
            {
                if (!location.IsInSource) return location.Kind.ToString();
                var solution = inPending ? pending : baseline;
                return solution.GetDocumentId(location.SourceTree!)?.ToString()
                    ?? location.SourceTree?.FilePath ?? string.Empty;
            }
            introduced.AddRange(InterfaceNullableProjection.AddedErrors(beforeErrors, afterErrors, LocationKey)
                .Select(diagnostic => (after.Name, diagnostic)));
        }

        if (introduced.Count > 0)
            throw new PublicInvalidOperationException(
                "The generated interface introduces compiler errors: " +
                string.Join("; ", introduced.GroupBy(item => item.Project, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key} ({InterfaceNullableProjection.FormatIds(group.Select(item => item.Diagnostic))})")) +
                ". Resolve the signature or choose a compatible receiving project before previewing again.");
    }
}
