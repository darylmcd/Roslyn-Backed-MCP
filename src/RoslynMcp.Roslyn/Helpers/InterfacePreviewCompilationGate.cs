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

    private const string LinkedCopyCause =
        "check every project that compiles an edited file (a file linked into several projects is edited in each)";

    /// <summary>Remedy for the same-project route when only a project that compiles a linked copy of an edited file broke.</summary>
    public const string LinkedCopyRemedy = "Resolve the reported errors in every project that compiles an edited file (a file linked into several projects is edited in each), before previewing again.";

    /// <summary>
    /// The remedy for a same-project refusal: it names every cause that applies to this request, and only those.
    /// <paramref name="linkedCopyChanged"/> is set when the edit reached a project other than the source's by copying an edited
    /// file to its linked copies; that project can fail on its own, whatever the other causes are.
    /// </summary>
    public static string RemedyFor(bool sameNamedType, bool replaceUsages, bool linkedCopyChanged)
    {
        if (!linkedCopyChanged)
        {
            if (!sameNamedType && !replaceUsages)
                throw InvalidOperationErrors.Internal("A same-project interface refusal needs at least one possible cause.");
            return !sameNamedType ? ReplaceUsagesRemedy : replaceUsages ? SiblingReplaceUsagesRemedy : SiblingRemedy;
        }

        if (!sameNamedType && !replaceUsages) return LinkedCopyRemedy;
        var causes = new List<string>();
        if (sameNamedType) causes.Add("choose a different interface name");
        if (replaceUsages)
            causes.Add("preview with replaceUsages=false or include the members those usages call in memberNames");
        causes.Add(LinkedCopyCause);
        return "Resolve the reported errors: " + string.Join("; or ", causes) + ", before previewing again.";
    }

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
