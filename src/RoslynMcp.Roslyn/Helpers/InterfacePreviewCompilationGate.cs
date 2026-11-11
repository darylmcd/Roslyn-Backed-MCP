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
        var introduced = await PreviewCompilationDelta.IntroducedErrorsAsync(baseline, pending, changed, LocationKey, ct)
            .ConfigureAwait(false);

        if (introduced.Count > 0)
            throw new PublicInvalidOperationException(
                "The generated interface introduces compiler errors: " +
                string.Join("; ", introduced.GroupBy(item => item.ProjectName, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key} ({InterfaceNullableProjection.FormatIds(group.Select(item => item.Diagnostic))})")) +
                ". Resolve the signature or choose a compatible receiving project before previewing again.");
    }

    private static string LocationKey(Solution solution, Location location)
    {
        if (!location.IsInSource) return location.Kind.ToString();
        return solution.GetDocumentId(location.SourceTree!)?.ToString()
            ?? location.SourceTree?.FilePath ?? string.Empty;
    }
}
