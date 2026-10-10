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

internal sealed class FixAllDiagnosticCollectionUnavailableException : InvalidOperationException;

internal sealed class FixAllDiagnosticCollector(ICompilationCache compilationCache)
{
    internal async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> CollectAsync(
        FixAllDiagnosticCollectionContext context,
        CancellationToken cancellationToken) =>
        await CollectCoreAsync(context, context.Solution, useCache: true, cancellationToken).ConfigureAwait(false)
        ?? throw new FixAllDiagnosticCollectionUnavailableException();

    /// <summary>
    /// Re-diagnoses the candidate snapshot without reading or modifying the live compilation cache.
    /// A missing required scope target or compilation makes verification unavailable, never empty.
    /// </summary>
    internal Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>?> CollectCandidateAsync(
        FixAllDiagnosticCollectionContext context,
        Solution candidate,
        CancellationToken cancellationToken) =>
        CollectCoreAsync(context, candidate, useCache: false, cancellationToken);

    private async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>?> CollectCoreAsync(
        FixAllDiagnosticCollectionContext context,
        Solution solution,
        bool useCache,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var originalProjects = context.Scope == FixAllScope.Solution
            ? context.Solution.Projects.ToArray()
            : [context.TargetProject];
        var originalDocumentIds = context.Scope == FixAllScope.Document
            ? [context.TargetDocument.Id]
            : originalProjects.SelectMany(project => project.DocumentIds).ToArray();
        if (originalDocumentIds.Any(id => solution.GetDocument(id) is null))
        {
            return null;
        }

        var builder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();
        var projects = originalProjects.Select(project => solution.GetProject(project.Id));

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project is null)
            {
                return null;
            }
            var compilation = useCache
                ? await compilationCache.GetCompilationAsync(context.WorkspaceId, project, cancellationToken).ConfigureAwait(false)
                : await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                return null;
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
