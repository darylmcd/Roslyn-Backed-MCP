using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Rejects new compiler errors in the final solution of an interface preview.</summary>
internal static class InterfacePreviewCompilationGate
{
    /// <summary>Remedy for routes that generate the interface into another project.</summary>
    public const string ReceivingProjectRemedy = "Resolve the signature or choose a compatible receiving project before previewing again.";

    /// <summary>Remedy for the same-project route when <c>replaceUsages</c> retyped a consumer into an error.</summary>
    public const string ReplaceUsagesRemedy =
        "Preview with replaceUsages=false, or include the members those usages call in memberNames, before previewing again.";

    /// <summary>Remedy for the same-project route when an implemented interface of the requested name already exists and its references were re-pinned.</summary>
    public const string SiblingRemedy = "Resolve the reported errors, or choose a different interface name, before previewing again.";

    public static async Task ValidateAsync(Solution baseline, Solution pending, string remedy, CancellationToken ct)
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
                $". {remedy}");
    }

    private static string LocationKey(Solution solution, Location location)
    {
        if (!location.IsInSource) return location.Kind.ToString();
        return solution.GetDocumentId(location.SourceTree!)?.ToString()
            ?? location.SourceTree?.FilePath ?? string.Empty;
    }
}
