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

    /// <summary>Remedy for the same-project route when a type of the requested name already exists (its references were re-pinned) and <c>replaceUsages</c> is off.</summary>
    public const string SiblingRemedy = "Resolve the reported errors, or choose a different interface name, before previewing again.";

    /// <summary>The same, when <c>replaceUsages</c> is on and could be the cause: both causes are named.</summary>
    public const string SiblingReplaceUsagesRemedy =
        "Resolve the reported errors, choose a different interface name, or, if replaceUsages retyped a consumer, " +
        "preview with replaceUsages=false or include the members those usages call in memberNames, before previewing again.";

    /// <summary>The remedy for a same-project refusal: which causes are possible decides which are named.</summary>
    public static string RemedyFor(bool sameNamedType, bool replaceUsages) =>
        !sameNamedType ? ReplaceUsagesRemedy : replaceUsages ? SiblingReplaceUsagesRemedy : SiblingRemedy;

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
