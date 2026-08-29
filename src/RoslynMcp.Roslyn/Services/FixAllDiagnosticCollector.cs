using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

internal sealed record FixAllDiagnosticCollectionContext(
    string WorkspaceId,
    Solution Solution,
    string DiagnosticId,
    FixAllScope Scope,
    Document TargetDocument,
    Project TargetProject,
    ImmutableArray<DiagnosticAnalyzer> Analyzers);

internal sealed class FixAllDiagnosticCollector(ICompilationCache compilationCache)
{
    internal async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> CollectAsync(
        FixAllDiagnosticCollectionContext context,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
        IEnumerable<Project> projects = context.Scope == FixAllScope.Solution
            ? context.Solution.Projects
            : [context.TargetProject];

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await compilationCache
                .GetCompilationAsync(context.WorkspaceId, project, cancellationToken)
                .ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            IEnumerable<Diagnostic> diagnostics;
            if (!context.Analyzers.IsDefaultOrEmpty)
            {
                var relevantAnalyzers = context.Analyzers
                    .Where(analyzer => analyzer.SupportedDiagnostics.Any(
                        descriptor => descriptor.Id == context.DiagnosticId))
                    .ToImmutableArray();

                if (!relevantAnalyzers.IsEmpty)
                {
                    var compilationWithAnalyzers = compilation.WithAnalyzers(relevantAnalyzers);
                    var analyzerDiagnostics = await compilationWithAnalyzers
                        .GetAnalyzerDiagnosticsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    diagnostics = analyzerDiagnostics.Where(IsRequestedSourceDiagnostic);
                }
                else
                {
                    diagnostics = compilation.GetDiagnostics(cancellationToken)
                        .Where(IsRequestedSourceDiagnostic);
                }
            }
            else
            {
                diagnostics = compilation.GetDiagnostics(cancellationToken)
                    .Where(IsRequestedSourceDiagnostic);
            }

            foreach (var group in diagnostics.GroupBy(diagnostic => diagnostic.Location.SourceTree))
            {
                if (group.Key is null)
                {
                    continue;
                }

                var document = project.GetDocument(group.Key);
                if (document is null ||
                    (context.Scope == FixAllScope.Document && document.Id != context.TargetDocument.Id))
                {
                    continue;
                }

                builder[document] = group.ToImmutableArray();
            }
        }

        return builder.ToImmutable();

        bool IsRequestedSourceDiagnostic(Diagnostic diagnostic) =>
            diagnostic.Id == context.DiagnosticId && diagnostic.Location.IsInSource;
    }
}
