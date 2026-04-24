using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Reflection;

namespace RoslynMcp.Roslyn.Services;

public sealed class FixAllService : IFixAllService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<FixAllService> _logger;
    private readonly Lazy<ImmutableArray<CodeFixProvider>> _codeFixProviders;
    private readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> _analyzers;

    public FixAllService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<FixAllService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
        _codeFixProviders = new Lazy<ImmutableArray<CodeFixProvider>>(LoadCodeFixProviders);
        _analyzers = new Lazy<ImmutableArray<DiagnosticAnalyzer>>(LoadAnalyzers);
    }

    /// <summary>
    /// Chooses which analyzers feed <see cref="CollectDiagnosticsAsync"/> for fix-all.
    /// IDE* rules merge Roslyn Features analyzers with project analyzers; all other IDs use
    /// project analyzers when any support the diagnostic (e.g. SCS*, MA*, third-party), else none
    /// (compiler-only fallback in collection).
    /// </summary>
    internal static ImmutableArray<DiagnosticAnalyzer> SelectAnalyzersForFixAllCollection(
        string diagnosticId,
        ImmutableArray<DiagnosticAnalyzer> ideFeaturesAnalyzers,
        ImmutableArray<DiagnosticAnalyzer> projectAnalyzers)
    {
        if (diagnosticId.StartsWith("IDE", StringComparison.OrdinalIgnoreCase))
        {
            var merged = new HashSet<DiagnosticAnalyzer>(ReferenceEqualityComparer.Instance);
            foreach (var a in ideFeaturesAnalyzers)
            {
                merged.Add(a);
            }

            foreach (var a in projectAnalyzers)
            {
                merged.Add(a);
            }

            return [..merged];
        }

        if (!projectAnalyzers.IsDefaultOrEmpty)
        {
            return projectAnalyzers;
        }

        return [];
    }

    public async Task<FixAllPreviewDto> PreviewFixAllAsync(
        string workspaceId, string diagnosticId, string scope,
        string? filePath, string? projectName, CancellationToken ct)
    {
        var fixAllScope = ParseScope(scope);
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var staticProviders = _codeFixProviders.Value;
        var analyzerAssemblyProviders = LoadCodeFixProvidersFromAnalyzerReferences(solution);
        var provider = FindCodeFixProvider(staticProviders, diagnosticId)
            ?? FindCodeFixProvider(analyzerAssemblyProviders, diagnosticId);
        if (provider is null)
        {
            var alternativeHint = GetAlternativeToolHint(diagnosticId);
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage:
                    $"No code fix provider is loaded for diagnostic '{diagnosticId}'. " +
                    (alternativeHint ?? "Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs. " +
                        "If this is an Info/IDE-series diagnostic without a built-in fix, consider add_pragma_suppression " +
                        "or an editorconfig severity bump via set_diagnostic_severity."));
        }

        var fixAllProvider = provider.GetFixAllProvider();
        if (fixAllProvider is null)
        {
            var hint = diagnosticId.StartsWith("IDE", StringComparison.OrdinalIgnoreCase)
                ? $"The IDE code fix provider for '{diagnosticId}' does not support FixAll in this workspace. " +
                  "For IDE0005 (unnecessary usings), use organize_usings_preview / organize_usings_apply instead."
                : $"The code fix provider for '{diagnosticId}' does not support FixAll; use code_fix_preview on individual instances or a narrower scope.";
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage: hint);
        }

        // Determine target document and project
        var (targetDocument, targetProject) = ResolveTargets(solution, fixAllScope, filePath, projectName);

        var projectAnalyzers = CollectProjectAnalyzersForDiagnosticId(solution, diagnosticId);
        var analyzersForCollection = SelectAnalyzersForFixAllCollection(
            diagnosticId, _analyzers.Value, projectAnalyzers);

        var diagnosticsMap = await CollectDiagnosticsAsync(
            solution, diagnosticId, fixAllScope, targetDocument, targetProject,
            analyzersForCollection, ct).ConfigureAwait(false);

        var totalDiagCount = diagnosticsMap.Values.Sum(d => d.Length);
        if (totalDiagCount == 0)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage: BuildNoOccurrencesGuidance(diagnosticId, scope, filePath, projectName));
        }

        // Obtain the correct equivalence key by invoking the provider on a sample diagnostic
        var equivalenceKey = await GetEquivalenceKeyAsync(provider, diagnosticId, diagnosticsMap, ct).ConfigureAwait(false);

        // Use the FixAllProvider to compute the fix
        var fixAllContext = new FixAllContext(
            document: targetDocument,
            codeFixProvider: provider,
            scope: fixAllScope,
            codeActionEquivalenceKey: equivalenceKey,
            diagnosticIds: [diagnosticId],
            fixAllDiagnosticProvider: new DiagnosticMapProvider(diagnosticsMap),
            cancellationToken: ct);

        CodeAction? fixAllAction;
        try
        {
            fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // fix-all-preview-sequence-contains-no-elements: FixAll providers (notably the
            // collection-expression fixer on IDE0300) can throw InvalidOperationException —
            // commonly "Sequence contains no elements" — when internal preconditions fail
            // on a specific occurrence. Narrow catch to InvalidOperationException only; other
            // exception types indicate bugs we want surfaced, not swallowed.
            _logger.LogWarning(ex,
                "FixAllProvider threw for diagnostic '{DiagnosticId}' at scope '{Scope}': {Message}",
                diagnosticId, scope, ex.Message);
            return BuildProviderCrashEnvelope(diagnosticId, scope, ex);
        }

        if (fixAllAction is null)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage: BuildProviderHasNoActionsGuidance(diagnosticId, totalDiagCount));
        }

        ImmutableArray<CodeActionOperation> operations;
        try
        {
            operations = await fixAllAction.GetOperationsAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // Same narrowing as the GetFixAsync call site above: InvalidOperationException is
            // the observed failure mode; broader catches would mask genuine defects.
            _logger.LogWarning(ex,
                "FixAll action GetOperationsAsync threw for '{DiagnosticId}': {Message}",
                diagnosticId, ex.Message);
            return BuildProviderCrashEnvelope(diagnosticId, scope, ex);
        }

        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOp is null)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage:
                    $"The FixAll action for '{diagnosticId}' returned no ApplyChangesOperation — the provider " +
                    "computed operations but none were workspace edits (typical for interactive-only or " +
                    "metadata-mutation fixes). Try code_fix_preview on individual occurrences to inspect the action.");
        }

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

    private static (Document targetDocument, Project targetProject) ResolveTargets(
        Solution solution, FixAllScope fixAllScope, string? filePath, string? projectName)
    {
        if (fixAllScope == FixAllScope.Document)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required when scope is 'document'.");

            var doc = SymbolResolver.FindDocument(solution, filePath)
                ?? throw new FileNotFoundException($"Document not found: {filePath}");
            return (doc, doc.Project);
        }

        if (fixAllScope == FixAllScope.Project)
        {
            var projects = ProjectFilterHelper.FilterProjects(solution, projectName);
            var proj = projects.FirstOrDefault()
                ?? throw new InvalidOperationException($"Project not found: {projectName}");
            var doc = proj.Documents.FirstOrDefault()
                ?? throw new InvalidOperationException("Project has no documents.");
            return (doc, proj);
        }

        // Solution scope
        var solutionProject = solution.Projects.FirstOrDefault()
            ?? throw new InvalidOperationException("Solution has no projects.");
        var solutionDoc = solutionProject.Documents.FirstOrDefault()
            ?? throw new InvalidOperationException("Solution has no documents.");
        return (solutionDoc, solutionProject);
    }

    private static FixAllScope ParseScope(string scope) => scope.ToLowerInvariant() switch
    {
        "document" => FixAllScope.Document,
        "project" => FixAllScope.Project,
        "solution" => FixAllScope.Solution,
        _ => throw new ArgumentException($"Invalid scope '{scope}'. Must be 'document', 'project', or 'solution'.")
    };

    /// <summary>
    /// Obtains the correct equivalence key by invoking the provider on a sample diagnostic.
    /// The FixAllProvider requires the exact key the provider registers — fabricated keys always fail.
    /// </summary>
    private static async Task<string> GetEquivalenceKeyAsync(
        CodeFixProvider provider, string diagnosticId,
        ImmutableDictionary<Document, ImmutableArray<Diagnostic>> diagnosticsMap,
        CancellationToken ct)
    {
        // Find the first diagnostic to use as a sample
        foreach (var (doc, diagnostics) in diagnosticsMap)
        {
            var sampleDiag = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
            if (sampleDiag is null) continue;

            var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
            string? capturedKey = null;

            var context = new CodeFixContext(doc, sampleDiag, (action, _) =>
            {
                capturedKey ??= action.EquivalenceKey;
            }, ct);

            try
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Some providers may fail on specific diagnostics; try the next one
                continue;
            }

            if (capturedKey is not null)
                return capturedKey;
        }

        // Fallback: use provider type name (may not work, but better than nothing)
        return provider.GetType().Name;
    }

    private static async Task<ImmutableDictionary<Document, ImmutableArray<Diagnostic>>> CollectDiagnosticsAsync(
        Solution solution, string diagnosticId, FixAllScope scope,
        Document targetDocument, Project targetProject,
        ImmutableArray<DiagnosticAnalyzer> analyzers, CancellationToken ct)
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

            IEnumerable<Diagnostic> allDiagnostics;

            if (!analyzers.IsDefaultOrEmpty)
            {
                // For IDE diagnostics, run analyzers to get them
                var relevantAnalyzers = analyzers
                    .Where(a => a.SupportedDiagnostics.Any(d => d.Id == diagnosticId))
                    .ToImmutableArray();

                if (!relevantAnalyzers.IsEmpty)
                {
                    var compilationWithAnalyzers = compilation.WithAnalyzers(relevantAnalyzers);
                    var analyzerDiags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
                    allDiagnostics = analyzerDiags
                        .Where(d => d.Id == diagnosticId && d.Location.IsInSource);
                }
                else
                {
                    // Fall back to compiler diagnostics
                    allDiagnostics = compilation.GetDiagnostics(ct)
                        .Where(d => d.Id == diagnosticId && d.Location.IsInSource);
                }
            }
            else
            {
                allDiagnostics = compilation.GetDiagnostics(ct)
                    .Where(d => d.Id == diagnosticId && d.Location.IsInSource);
            }

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

    private static CodeFixProvider? FindCodeFixProvider(ImmutableArray<CodeFixProvider> providers, string diagnosticId) =>
        providers.FirstOrDefault(p => p.FixableDiagnosticIds.Contains(diagnosticId));

    private ImmutableArray<CodeFixProvider> LoadCodeFixProvidersFromAnalyzerReferences(Solution solution)
    {
        var list = new List<CodeFixProvider>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            foreach (var ar in project.AnalyzerReferences)
            {
                if (ar is not AnalyzerFileReference afr)
                    continue;
                var analyzerPath = afr.Display;
                if (string.IsNullOrWhiteSpace(analyzerPath) || !paths.Add(analyzerPath))
                    continue;
                try
                {
                    var assembly = Assembly.LoadFrom(analyzerPath);
                    foreach (var t in assembly.GetTypes())
                    {
                        if (t.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(t))
                            continue;
                        if (t.GetConstructor(Type.EmptyTypes) is null)
                            continue;
                        if (Activator.CreateInstance(t) is not CodeFixProvider p)
                            continue;
                        list.Add(p);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Could not load code fix providers from analyzer assembly {Path}", analyzerPath);
                }
            }
        }

        if (list.Count > 0)
            _logger.LogInformation("FixAllService loaded {Count} code fix provider(s) from project analyzer references", list.Count);

        return [..list];
    }

    private static ImmutableArray<DiagnosticAnalyzer> CollectProjectAnalyzersForDiagnosticId(
        Solution solution, string diagnosticId)
    {
        var set = new HashSet<DiagnosticAnalyzer>(ReferenceEqualityComparer.Instance);
        foreach (var project in solution.Projects)
        {
            // unresolved-analyzer-reference-crash: WorkspaceManager.StripUnresolvedAnalyzerReferences
            // removes UnresolvedAnalyzerReference entries at load time, so the previous FLAG-A
            // filter here is no longer required.
            foreach (var ar in project.AnalyzerReferences)
            {
                foreach (var a in ar.GetAnalyzers(project.Language))
                {
                    if (a.SupportedDiagnostics.Any(d => d.Id == diagnosticId))
                        set.Add(a);
                }
            }
        }

        return [..set];
    }

    private static Assembly? LoadFeaturesAssembly()
    {
        try
        {
            return Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private ImmutableArray<CodeFixProvider> LoadCodeFixProviders()
    {
        try
        {
            var featuresAssembly = LoadFeaturesAssembly();
            if (featuresAssembly is null)
            {
                _logger.LogWarning("Could not load Microsoft.CodeAnalysis.CSharp.Features assembly");
                return [];
            }

            var candidateTypes = featuresAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            var providers = candidateTypes
                .Select(t =>
                {
                    try { return (CodeFixProvider?)Activator.CreateInstance(t); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
                })
                .Where(p => p is not null)
                .Cast<CodeFixProvider>()
                .ToImmutableArray();

            var skipped = candidateTypes.Count - providers.Length;
            _logger.LogInformation(
                "FixAllService loaded {Loaded} code fix providers from CSharp Features ({Skipped} skipped — no parameterless constructor)",
                providers.Length, skipped);
            return providers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load code fix providers for FixAll");
            return [];
        }
    }

    private ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        try
        {
            var featuresAssembly = LoadFeaturesAssembly();
            if (featuresAssembly is null) return [];

            var analyzers = featuresAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t =>
                {
                    try { return (DiagnosticAnalyzer?)Activator.CreateInstance(t); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
                })
                .Where(a => a is not null)
                .Cast<DiagnosticAnalyzer>()
                .ToImmutableArray();

            _logger.LogInformation("FixAllService loaded {Count} diagnostic analyzers from CSharp Features", analyzers.Length);
            return analyzers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load diagnostic analyzers for FixAll");
            return [];
        }
    }

    /// <summary>
    /// Builds the guidance message for the "no occurrences" empty-result path. A provider IS
    /// registered for the diagnostic, but <see cref="CollectDiagnosticsAsync"/> found zero
    /// occurrences in the requested scope. This distinguishes scenario (1) from scenarios (2)
    /// "no provider registered" and (3) "provider returned no actions" so the caller can tell
    /// them apart.
    /// </summary>
    internal static string BuildNoOccurrencesGuidance(
        string diagnosticId, string scope, string? filePath, string? projectName)
    {
        var scopeSuffix = scope.ToLowerInvariant() switch
        {
            "document" when !string.IsNullOrWhiteSpace(filePath) => $" (document scope: '{filePath}')",
            "project" when !string.IsNullOrWhiteSpace(projectName) => $" (project scope: '{projectName}')",
            "solution" => " (solution scope)",
            _ => string.Empty,
        };

        return
            $"No occurrences of '{diagnosticId}' found in the requested scope{scopeSuffix}. " +
            "A code fix provider IS registered for this diagnostic — the workspace simply has no matches. " +
            "If you expected matches, verify the diagnostic is currently reported via project_diagnostics " +
            "or list_analyzers.";
    }

    /// <summary>
    /// Builds the guidance message for the "provider registered, occurrences exist, but no
    /// CodeAction produced" path. This can happen when the provider's Fixable check accepts
    /// the diagnostic id globally but rejects each occurrence's context at registration time
    /// (e.g. syntax-shape preconditions inside the provider).
    /// </summary>
    internal static string BuildProviderHasNoActionsGuidance(string diagnosticId, int occurrenceCount)
    {
        return
            $"The provider for '{diagnosticId}' produced no FixAll action for {occurrenceCount} occurrence(s). " +
            "This typically means the provider's internal Fixable check rejected every occurrence's syntax " +
            "context. Try code_fix_preview on individual occurrences to inspect per-site behaviour, " +
            "or add_pragma_suppression / set_diagnostic_severity if the rule cannot be auto-fixed here.";
    }

    /// <summary>
    /// Builds the structured error envelope returned when the registered <c>FixAllProvider</c>
    /// throws <see cref="InvalidOperationException"/> while computing the fix or materialising
    /// operations. This includes the well-known <c>"Sequence contains no elements"</c> crash on
    /// IDE0300 (use-collection-expression) and analogous failures on other fixers whose internal
    /// invariants reject specific occurrences.
    /// </summary>
    /// <remarks>
    /// Callers inspect <see cref="FixAllPreviewDto.Error"/> and
    /// <see cref="FixAllPreviewDto.Category"/> to distinguish a provider crash from a missing
    /// provider, zero occurrences, or a provider that silently produced no actions.
    /// <see cref="FixAllPreviewDto.PerOccurrenceFallbackAvailable"/> signals that calling
    /// <c>code_fix_preview</c> per occurrence is a viable recovery path.
    /// </remarks>
    internal static FixAllPreviewDto BuildProviderCrashEnvelope(
        string diagnosticId, string scope, Exception ex)
    {
        var message =
            $"The registered FixAll provider for '{diagnosticId}' threw while computing the fix " +
            $"({ex.GetType().Name}: {ex.Message}). Try code_fix_preview on individual occurrences, " +
            "or narrow the scope (document / project) to isolate the failing occurrence.";

        return new FixAllPreviewDto(
            PreviewToken: "",
            DiagnosticId: diagnosticId,
            Scope: scope,
            FixedCount: 0,
            Changes: [],
            GuidanceMessage: message,
            Error: true,
            Category: "FixAllProviderCrash",
            PerOccurrenceFallbackAvailable: true);
    }

    /// <summary>
    /// Returns an alternative tool suggestion for known IDE diagnostics that lack FixAll providers.
    /// Many IDE code fix providers require constructor parameters that cannot be satisfied via
    /// reflection instantiation, so they are silently skipped. This mapping directs agents to
    /// the correct alternative tool or manual workaround.
    /// </summary>
    private static string? GetAlternativeToolHint(string diagnosticId) =>
        diagnosticId.ToUpperInvariant() switch
        {
            "IDE0005" => "Use organize_usings_preview / organize_usings_apply to remove unused usings.",
            "IDE0007" or "IDE0008" => "Use 'var' vs explicit type preferences are code style settings. Apply manually or use code_fix_preview on individual instances.",
            "IDE0055" => "Use format_document_preview / format_document_apply for formatting fixes.",
            "IDE0160" or "IDE0161" => "Block-scoped vs file-scoped namespace preferences must be applied manually or with code_fix_preview on individual instances.",
            "IDE0290" => "Primary constructor conversion must be applied manually or with code_fix_preview on individual instances.",
            _ when diagnosticId.StartsWith("IDE", StringComparison.OrdinalIgnoreCase) =>
                $"The IDE code fix provider for '{diagnosticId}' could not be loaded (constructor requirements). " +
                "Try code_fix_preview on individual instances, or use list_analyzers to check if the diagnostic is present.",
            _ => null
        };

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
