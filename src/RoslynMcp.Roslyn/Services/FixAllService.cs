using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace RoslynMcp.Roslyn.Services;

public sealed class FixAllService : IFixAllService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<FixAllService> _logger;
    private readonly Lazy<ImmutableArray<CodeFixProvider>> _codeFixProviders;

    public FixAllService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<FixAllService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
        _codeFixProviders = new Lazy<ImmutableArray<CodeFixProvider>>(LoadCodeFixProviders);
    }

    public async Task<FixAllPreviewDto> PreviewFixAllAsync(
        string workspaceId, string diagnosticId, string scope,
        string? filePath, string? projectName, CancellationToken ct)
    {
        var fixAllScope = ParseScope(scope);
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Find a provider that can fix this diagnostic
        var provider = _codeFixProviders.Value
            .FirstOrDefault(p => p.FixableDiagnosticIds.Contains(diagnosticId))
            ?? throw new InvalidOperationException(
                $"No code fix provider found for diagnostic '{diagnosticId}'. " +
                "Use list_analyzers to see available diagnostic IDs.");

        var fixAllProvider = provider.GetFixAllProvider()
            ?? throw new InvalidOperationException(
                $"The code fix provider for '{diagnosticId}' does not support FixAll operations.");

        // Determine target document and project
        Document? targetDocument = null;
        Project? targetProject = null;

        if (fixAllScope == FixAllScope.Document)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required when scope is 'document'.");

            targetDocument = SymbolResolver.FindDocument(solution, filePath)
                ?? throw new FileNotFoundException($"Document not found: {filePath}");
            targetProject = targetDocument.Project;
        }
        else if (fixAllScope == FixAllScope.Project)
        {
            var projects = ProjectFilterHelper.FilterProjects(solution, projectName);
            targetProject = projects.FirstOrDefault()
                ?? throw new InvalidOperationException($"Project not found: {projectName}");

            // Need a document for the context; use the first one
            targetDocument = targetProject.Documents.FirstOrDefault()
                ?? throw new InvalidOperationException("Project has no documents.");
        }
        else // Solution
        {
            targetProject = solution.Projects.FirstOrDefault()
                ?? throw new InvalidOperationException("Solution has no projects.");
            targetDocument = targetProject.Documents.FirstOrDefault()
                ?? throw new InvalidOperationException("Solution has no documents.");
        }

        // Collect all diagnostics matching the ID across the scope
        var diagnosticsMap = await CollectDiagnosticsAsync(
            solution, diagnosticId, fixAllScope, targetDocument, targetProject, ct).ConfigureAwait(false);

        var totalDiagCount = diagnosticsMap.Values.Sum(d => d.Length);
        if (totalDiagCount == 0)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: []);
        }

        // Use the FixAllProvider to compute the fix
        var fixAllContext = new FixAllContext(
            document: targetDocument,
            codeFixProvider: provider,
            scope: fixAllScope,
            codeActionEquivalenceKey: provider.GetType().Name + "." + diagnosticId,
            diagnosticIds: [diagnosticId],
            fixAllDiagnosticProvider: new DiagnosticMapProvider(diagnosticsMap),
            cancellationToken: ct);

        var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
        if (fixAllAction is null)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: []);
        }

        var operations = await fixAllAction.GetOperationsAsync(ct).ConfigureAwait(false);
        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault()
            ?? throw new InvalidOperationException("FixAll action did not produce workspace changes.");

        var newSolution = applyOp.ChangedSolution;
        var changes = await ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Fix all '{diagnosticId}' ({scope}): {totalDiagCount} occurrences";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new FixAllPreviewDto(
            PreviewToken: token,
            DiagnosticId: diagnosticId,
            Scope: scope,
            FixedCount: totalDiagCount,
            Changes: changes);
    }

    private static FixAllScope ParseScope(string scope) => scope.ToLowerInvariant() switch
    {
        "document" => FixAllScope.Document,
        "project" => FixAllScope.Project,
        "solution" => FixAllScope.Solution,
        _ => throw new ArgumentException($"Invalid scope '{scope}'. Must be 'document', 'project', or 'solution'.")
    };

    private static async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> CollectDiagnosticsAsync(
        Solution solution, string diagnosticId, FixAllScope scope,
        Document targetDocument, Project targetProject, CancellationToken ct)
    {
        var builder = ImmutableDictionary.CreateBuilder<Document, ImmutableArray<Diagnostic>>();

        IEnumerable<Project> projects = scope == FixAllScope.Solution
            ? solution.Projects
            : [targetProject];

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var allDiagnostics = compilation.GetDiagnostics(ct)
                .Where(d => d.Id == diagnosticId && d.Location.IsInSource);

            var byTree = allDiagnostics.GroupBy(d => d.Location.SourceTree);

            foreach (var group in byTree)
            {
                if (group.Key is null) continue;

                var doc = project.GetDocument(group.Key);
                if (doc is null) continue;

                if (scope == FixAllScope.Document && doc.Id != targetDocument.Id)
                    continue;

                builder[doc] = group.ToImmutableArray();
            }
        }

        return builder.ToImmutable();
    }

    private static async Task<IReadOnlyList<FileChangeDto>> ComputeChangesAsync(
        Solution oldSolution, Solution newSolution, CancellationToken ct)
    {
        var changes = new List<FileChangeDto>();
        var solutionChanges = newSolution.GetChanges(oldSolution);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = oldSolution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                var oldText = (await oldDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                var newText = (await newDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();

                if (oldText == newText) continue;

                var path = oldDoc.FilePath ?? oldDoc.Name;
                var diff = DiffGenerator.GenerateUnifiedDiff(oldText, newText, path);
                changes.Add(new FileChangeDto(path, diff));
            }
        }

        return changes;
    }

    private ImmutableArray<CodeFixProvider> LoadCodeFixProviders()
    {
        try
        {
            var featuresAssembly = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly;
            var providers = featuresAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
                .Select(t =>
                {
                    try { return (CodeFixProvider?)Activator.CreateInstance(t); }
                    catch { return null; }
                })
                .Where(p => p is not null)
                .Cast<CodeFixProvider>()
                .ToImmutableArray();

            _logger.LogInformation("FixAllService loaded {Count} code fix providers", providers.Length);
            return providers;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load code fix providers for FixAll");
            return [];
        }
    }

    /// <summary>
    /// Provides pre-computed diagnostics to the FixAllContext.
    /// </summary>
    private sealed class DiagnosticMapProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableDictionary<Document, ImmutableArray<Diagnostic>> _map;

        public DiagnosticMapProvider(ImmutableDictionary<Document, ImmutableArray<Diagnostic>> map) => _map = map;

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken ct)
        {
            var result = _map
                .Where(kvp => kvp.Key.Project.Id == project.Id)
                .SelectMany(kvp => kvp.Value);
            return Task.FromResult(result);
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken ct)
        {
            if (_map.TryGetValue(document, out var diagnostics))
                return Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
            return Task.FromResult<IEnumerable<Diagnostic>>([]);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken ct)
        {
            // Project-level diagnostics (not tied to a document)
            return Task.FromResult<IEnumerable<Diagnostic>>([]);
        }
    }
}
