using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Locates one compiler or analyzer diagnostic at an exact source position.
/// </summary>
internal sealed class DiagnosticDocumentLookup(ICompilationCache compilationCache)
{
    private readonly ICompilationCache _compilationCache = compilationCache;

    public async Task<DiagnosticLookupResult> FindAsync(
        string workspaceId,
        Solution solution,
        DiagnosticLookupTarget target,
        IReadOnlyList<Diagnostic>? cachedDiagnostics,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (cachedDiagnostics is not null)
        {
            var cached = FindMatch(
                cachedDiagnostics,
                target);
            if (cached is not null)
            {
                return new DiagnosticLookupResult(cached, FullScanDiagnostics: null);
            }
        }

        var documentDiagnostic = await FindInDocumentAsync(
            workspaceId,
            solution,
            target,
            ct).ConfigureAwait(false);
        if (documentDiagnostic is not null)
        {
            return new DiagnosticLookupResult(
                documentDiagnostic,
                FullScanDiagnostics: null);
        }

        var allDiagnostics = await CollectSolutionDiagnosticsAsync(
            workspaceId,
            solution,
            ct).ConfigureAwait(false);
        return new DiagnosticLookupResult(
            FindMatch(allDiagnostics, target),
            allDiagnostics);
    }

    private async Task<Diagnostic?> FindInDocumentAsync(
        string workspaceId,
        Solution solution,
        DiagnosticLookupTarget target,
        CancellationToken ct)
    {
        var documentIds = solution.GetDocumentIdsWithFilePath(target.FilePath);
        if (documentIds.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var documentId in documentIds)
        {
            ct.ThrowIfCancellationRequested();
            var document = solution.GetDocument(documentId);
            if (document is null)
            {
                continue;
            }

            var match = await FindInResolvedDocumentAsync(
                workspaceId,
                document,
                target,
                ct).ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<Diagnostic?> FindInResolvedDocumentAsync(
        string workspaceId,
        Document document,
        DiagnosticLookupTarget target,
        CancellationToken ct)
    {
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null)
        {
            return null;
        }

        var compilation = await _compilationCache
            .GetCompilationAsync(workspaceId, document.Project, ct)
            .ConfigureAwait(false);
        if (compilation is null)
        {
            return null;
        }

        // Compiler diagnostics take precedence over analyzer diagnostics at the same
        // location, preserving the service's long-standing selection contract.
        var compilerMatch = FindMatch(
            compilation.GetDiagnostics(ct).Where(diagnostic =>
                diagnostic.Location.SourceTree == tree),
            target);
        return compilerMatch ?? await FindAnalyzerMatchAsync(
            workspaceId,
            document,
            tree,
            target,
            ct).ConfigureAwait(false);
    }

    private async Task<Diagnostic?> FindAnalyzerMatchAsync(
        string workspaceId,
        Document document,
        SyntaxTree tree,
        DiagnosticLookupTarget target,
        CancellationToken ct)
    {
        var compilationWithAnalyzers = await _compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, document.Project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is null)
        {
            return null;
        }

        var syntaxDiagnostics = await compilationWithAnalyzers
            .GetAnalyzerSyntaxDiagnosticsAsync(tree, ct)
            .ConfigureAwait(false);
        var syntaxMatch = FindMatch(syntaxDiagnostics, target);
        if (syntaxMatch is not null)
        {
            return syntaxMatch;
        }

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        var semanticDiagnostics = await compilationWithAnalyzers
            .GetAnalyzerSemanticDiagnosticsAsync(
                semanticModel,
                filterSpan: null,
                ct)
            .ConfigureAwait(false);
        return FindMatch(semanticDiagnostics, target);
    }

    private async Task<IReadOnlyList<Diagnostic>> CollectSolutionDiagnosticsAsync(
        string workspaceId,
        Solution solution,
        CancellationToken ct)
    {
        var projectTasks = solution.Projects.Select(project =>
            CollectProjectDiagnosticsAsync(workspaceId, project, ct));
        var perProjectResults = await Task.WhenAll(projectTasks).ConfigureAwait(false);
        return perProjectResults.SelectMany(diagnostics => diagnostics).ToList();
    }

    private async Task<IReadOnlyList<Diagnostic>> CollectProjectDiagnosticsAsync(
        string workspaceId,
        Project project,
        CancellationToken ct)
    {
        var compilation = await _compilationCache
            .GetCompilationAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilation is null)
        {
            return [];
        }

        var collected = new List<Diagnostic>();
        collected.AddRange(compilation.GetDiagnostics(ct));

        var compilationWithAnalyzers = await _compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is null)
        {
            return collected;
        }

        collected.AddRange(await compilationWithAnalyzers
            .GetAllDiagnosticsAsync(ct)
            .ConfigureAwait(false));
        return collected;
    }

    private static Diagnostic? FindMatch(
        IEnumerable<Diagnostic> diagnostics,
        DiagnosticLookupTarget target) =>
        diagnostics.FirstOrDefault(diagnostic =>
            MatchesLocation(diagnostic, target));

    private static bool MatchesLocation(
        Diagnostic diagnostic,
        DiagnosticLookupTarget target)
    {
        if (!string.Equals(
                diagnostic.Id,
                target.DiagnosticId,
                StringComparison.OrdinalIgnoreCase)
            || !diagnostic.Location.IsInSource)
        {
            return false;
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        return string.Equals(
                Path.GetFullPath(lineSpan.Path),
                Path.GetFullPath(target.FilePath),
                StringComparison.OrdinalIgnoreCase)
            && lineSpan.StartLinePosition.Line + 1 == target.Line
            && lineSpan.StartLinePosition.Character + 1 == target.Column;
    }
}

internal sealed record DiagnosticLookupResult(
    Diagnostic? Diagnostic,
    IReadOnlyList<Diagnostic>? FullScanDiagnostics);

internal sealed record DiagnosticLookupTarget(
    string DiagnosticId,
    string FilePath,
    int Line,
    int Column);
