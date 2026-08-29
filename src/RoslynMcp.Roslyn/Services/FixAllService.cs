using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
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
    private readonly FixAllDiagnosticCollector _diagnosticCollector;
    private readonly ILogger<FixAllService> _logger;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly Lazy<FeatureProviderLoadResult<CodeFixProvider>> _codeFixProviders;
    private readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> _analyzers;

    public FixAllService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache,
        ILogger<FixAllService> logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        : this(workspace, previewStore, compilationCache, logger, exceptionReporter, null)
    {
    }

    internal FixAllService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache,
        ILogger<FixAllService> logger,
        ImmutableArray<CodeFixProvider> codeFixProviders,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        : this(
            workspace,
            previewStore,
            compilationCache,
            logger,
            exceptionReporter,
            new FeatureProviderLoadResult<CodeFixProvider>(codeFixProviders, []))
    {
    }

    private FixAllService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache,
        ILogger<FixAllService> logger,
        IUnexpectedExceptionReporter? exceptionReporter,
        FeatureProviderLoadResult<CodeFixProvider>? codeFixProviderOverride)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _diagnosticCollector = new FixAllDiagnosticCollector(compilationCache);
        _logger = logger;
        _exceptionReporter = exceptionReporter;
        _codeFixProviders = new Lazy<FeatureProviderLoadResult<CodeFixProvider>>(
            () => codeFixProviderOverride ??
                  CSharpFeatureProviderLoader.Load<CodeFixProvider>(_logger, _exceptionReporter));
        _analyzers = new Lazy<ImmutableArray<DiagnosticAnalyzer>>(
            () => codeFixProviderOverride is null ? LoadAnalyzers() : []);
    }

    /// <summary>
    /// Chooses which analyzers feed <see cref="FixAllDiagnosticCollector.CollectAsync"/> for fix-all.
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

            return [.. merged];
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
        var targetRequest = FixAllTargetResolver.ParseAndValidate(scope, filePath, projectName);
        var fixAllScope = targetRequest.Scope;
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var staticProviders = _codeFixProviders.Value.Providers;
        var analyzerAssemblyProviders = LoadCodeFixProvidersFromAnalyzerReferences(solution);
        var provider = FindCodeFixProvider(staticProviders, diagnosticId)
            ?? FindCodeFixProvider(analyzerAssemblyProviders.Providers, diagnosticId);
        if (provider is null)
        {
            return new FixAllPreviewDto(
                PreviewToken: "",
                DiagnosticId: diagnosticId,
                Scope: scope,
                FixedCount: 0,
                Changes: [],
                GuidanceMessage: BuildNoProviderGuidance(diagnosticId));
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
        var target = FixAllTargetResolver.Resolve(solution, targetRequest);

        var projectAnalyzers = CollectProjectAnalyzersForDiagnosticId(solution, diagnosticId);
        var analyzersForCollection = SelectAnalyzersForFixAllCollection(
            diagnosticId, _analyzers.Value, projectAnalyzers);

        var diagnosticsMap = await _diagnosticCollector.CollectAsync(
            new FixAllDiagnosticCollectionContext(
                workspaceId,
                solution,
                diagnosticId,
                fixAllScope,
                target.Document,
                target.Project,
                analyzersForCollection),
            ct).ConfigureAwait(false);

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
            document: target.Document,
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
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Fix all '{diagnosticId}' ({scope}): {totalDiagCount} occurrences";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            // preview-token-apply-route-provenance: `diffTruncated: false` preserves the exact
            // semantic of the prior 4-argument call (PreviewStore defaults the flag to false);
            // `kind` lets fix_all_apply refuse a token minted by a different producer family.
            diffTruncated: false,
            kind: PreviewKind.FixAll);

        return new FixAllPreviewDto(
            PreviewToken: token,
            DiagnosticId: diagnosticId,
            Scope: scope,
            FixedCount: totalDiagCount,
            Changes: changes);
    }

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

    private static CodeFixProvider? FindCodeFixProvider(ImmutableArray<CodeFixProvider> providers, string diagnosticId) =>
        providers.FirstOrDefault(p => p.FixableDiagnosticIds.Contains(diagnosticId));

    private FeatureProviderLoadResult<CodeFixProvider> LoadCodeFixProvidersFromAnalyzerReferences(Solution solution)
    {
        var providers = ImmutableArray.CreateBuilder<CodeFixProvider>();
        var failures = ImmutableArray.CreateBuilder<FeatureProviderLoadFailure>();
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
                var result = CSharpFeatureProviderLoader.LoadFromAssemblyFactory<CodeFixProvider>(
                    () => Assembly.LoadFrom(analyzerPath),
                    _logger,
                    _exceptionReporter);
                providers.AddRange(result.Providers);
                failures.AddRange(result.Failures);
            }
        }

        return new FeatureProviderLoadResult<CodeFixProvider>(
            providers.ToImmutable(),
            failures.ToImmutable());
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

        return [.. set];
    }

    private ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers() =>
        CSharpFeatureProviderLoader.Load<DiagnosticAnalyzer>(_logger, _exceptionReporter).Providers;

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
        string diagnosticId,
        string scope,
        PublicUnexpectedExceptionDetail detail)
    {
        var message =
            $"The registered FixAll provider for '{diagnosticId}' failed while computing the fix. " +
            "Try code_fix_preview on individual occurrences, or narrow the scope (document / project) " +
            $"to isolate the failing occurrence. correlationId={detail.CorrelationId}";

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

    internal FixAllPreviewDto BuildProviderCrashEnvelope(
        string diagnosticId,
        string scope,
        Exception exception)
    {
        var detail = UnexpectedExceptionReporting.Report(
            _exceptionReporter,
            exception,
            UnexpectedExceptionCategory.FixAll).Public;
        _logger.LogWarning(
            "FixAll provider failed for diagnostic '{DiagnosticId}' at scope '{Scope}'; correlationId={CorrelationId}",
            diagnosticId,
            scope,
            detail.CorrelationId);
        return BuildProviderCrashEnvelope(diagnosticId, scope, detail);
    }

    /// <summary>
    /// Builds the uniform "no fix provider loaded" guidance returned when neither the
    /// reflection-loaded CSharp.Features fix providers nor the project's analyzer-reference
    /// fix providers cover <paramref name="diagnosticId"/>. The message has the same structural
    /// shape regardless of the diagnostic's severity or whether the id is IDE-prefixed: it
    /// always names the diagnostic, calls out <c>list_analyzers</c> as the discovery tool, and
    /// suggests <c>add_pragma_suppression</c> / <c>set_diagnostic_severity</c> as fallbacks.
    /// When a known id has a more specific alternative tool (e.g. <c>organize_usings_preview</c>
    /// for <c>IDE0005</c>), that hint is appended.
    /// </summary>
    /// <remarks>
    /// fix-all-preview-silent-on-missing-provider-info-severity: previously the IDE-prefixed
    /// switch arm in <see cref="GetAlternativeToolHint"/> displaced the <c>list_analyzers</c>
    /// fallback, so Info-severity ids like <c>IDE0130</c>/<c>IDE0290</c>/<c>IDE0008</c>
    /// returned a guidance shape that omitted the <c>list_analyzers</c> pointer that
    /// non-IDE Warning ids (<c>CA1826</c>/<c>xUnit1051</c>) carried. Severity and id-prefix
    /// must not gate the <c>list_analyzers</c> reference — the baseline guidance is uniform,
    /// and id-specific tool hints are additive.
    /// </remarks>
    internal static string BuildNoProviderGuidance(string diagnosticId)
    {
        var baseline =
            $"No code fix provider is loaded for diagnostic '{diagnosticId}'. " +
            "Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs. " +
            "If this diagnostic has no built-in fix, consider add_pragma_suppression " +
            "or an editorconfig severity bump via set_diagnostic_severity.";

        var hint = GetAlternativeToolHint(diagnosticId);
        return hint is null ? baseline : baseline + " " + hint;
    }

    /// <summary>
    /// Returns an alternative tool suggestion for known IDE diagnostics that lack FixAll providers.
    /// Many IDE code fix providers require constructor parameters that cannot be satisfied via
    /// reflection instantiation, so they are silently skipped. This mapping directs agents to
    /// the correct alternative tool or manual workaround.
    /// </summary>
    /// <remarks>
    /// This helper returns ONLY id-specific tool hints — the generic "no provider loaded /
    /// use list_analyzers" baseline lives in <see cref="BuildNoProviderGuidance"/> and is
    /// emitted unconditionally. Adding a new id-specific hint here will append it to the
    /// uniform baseline; do NOT re-state the baseline pointers in the per-id strings.
    /// </remarks>
    private static string? GetAlternativeToolHint(string diagnosticId) =>
        diagnosticId.ToUpperInvariant() switch
        {
            "IDE0005" => "Use organize_usings_preview / organize_usings_apply to remove unused usings.",
            "IDE0007" or "IDE0008" => "Use 'var' vs explicit type preferences are code style settings. Apply manually or use code_fix_preview on individual instances.",
            "IDE0055" => "Use format_document_preview / format_document_apply for formatting fixes.",
            "IDE0160" or "IDE0161" => "Block-scoped vs file-scoped namespace preferences must be applied manually or with code_fix_preview on individual instances.",
            "IDE0290" => "Primary constructor conversion must be applied manually or with code_fix_preview on individual instances.",
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
