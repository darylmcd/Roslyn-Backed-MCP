using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Projects unresolved analyzer references out of loaded snapshots without writing authored files.</summary>
internal static class UnresolvedAnalyzerReferenceStripper
{
    /// <summary>Applies the loaded-workspace analyzer policy without writing files or publishing diagnostics.</summary>
    internal static Projection Project(Solution originalSolution, CancellationToken ct)
    {
        var solution = originalSolution;
        var newDiagnostics = new List<DiagnosticDto>();

        foreach (var project in originalSolution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var unresolved = project.AnalyzerReferences
                .OfType<UnresolvedAnalyzerReference>()
                .ToList();

            if (unresolved.Count == 0) continue;

            foreach (var reference in unresolved)
            {
                solution = solution.RemoveAnalyzerReference(project.Id, reference);

                var displayName = reference.Display ?? reference.FullPath ?? "<unknown>";
                newDiagnostics.Add(new DiagnosticDto(
                    Id: "WORKSPACE_UNRESOLVED_ANALYZER",
                    Message: $"Unresolved analyzer reference removed from project '{project.Name}': {displayName}. " +
                             "Run `dotnet build` on the analyzer project to produce the missing analyzer output, " +
                             "then run `workspace_reload`.",
                    Severity: WorkspaceDiagnosticSeverityClassifier.Classify(WorkspaceDiagnosticKind.Warning, ""),
                    Category: "Workspace",
                    FilePath: project.FilePath,
                    StartLine: null,
                    StartColumn: null,
                    EndLine: null,
                    EndColumn: null,
                    Location: null));
            }
        }

        return new Projection(solution, newDiagnostics);
    }

    internal sealed record Projection(Solution Solution, IReadOnlyList<DiagnosticDto> Diagnostics);

}
