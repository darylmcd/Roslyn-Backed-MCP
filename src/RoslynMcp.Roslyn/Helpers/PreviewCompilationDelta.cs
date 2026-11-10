using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>A compiler error present in a pending solution that the baseline did not have.</summary>
internal readonly record struct IntroducedCompilerError(ProjectId ProjectId, string ProjectName, Diagnostic Diagnostic);

/// <summary>
/// Compile-delta core shared by every preview that must not break the build: compiles each changed project and every
/// project that transitively depends on one, then diffs the error sets with
/// <see cref="InterfaceNullableProjection.AddedErrors"/> (multiplicity-aware, position-independent).
/// Callers own the refusal message; the location-key function is a parameter because the callers' fallbacks for
/// locations without a document id differ.
/// </summary>
internal static class PreviewCompilationDelta
{
    /// <param name="locationKey">Stable identity of a diagnostic location within the given solution (baseline or pending).</param>
    public static async Task<IReadOnlyList<IntroducedCompilerError>> IntroducedErrorsAsync(
        Solution baseline,
        Solution pending,
        IReadOnlyCollection<ProjectId> changedProjects,
        Func<Solution, Location, string> locationKey,
        CancellationToken ct)
    {
        var affected = changedProjects.ToHashSet();
        var graph = pending.GetProjectDependencyGraph();
        foreach (var id in changedProjects)
            affected.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(id));

        var introduced = new List<IntroducedCompilerError>();
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
            introduced.AddRange(InterfaceNullableProjection.AddedErrors(
                    beforeErrors, afterErrors, (location, inPending) => locationKey(inPending ? pending : baseline, location))
                .Select(diagnostic => new IntroducedCompilerError(id, after.Name, diagnostic)));
        }

        return introduced;
    }
}
