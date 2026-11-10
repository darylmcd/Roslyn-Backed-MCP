using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class TestDiscoveryService : ITestDiscoveryService, IDisposable
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<TestDiscoveryService> _logger;
    private readonly ValidationServiceOptions _options;

    // Per-workspace discovery cache. Every access goes through _cacheSync so the publish-time liveness
    // check (see PublishIfWorkspaceLive) and the WorkspaceClosed/WorkspaceReloaded eviction are atomic
    // with each other: a close that lands while discovery is in flight can never be followed by a
    // republish of that closed session.
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, (int Version, TestDiscoveryDto Result)> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Test seam for the semantic reference sweep. Every sweep site (symbol mode, direct-reference,
    /// inbound-reference) goes through this delegate so a test can inject a failing sweep.
    /// </summary>
    internal Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>> FindReferencesAsync { get; set; }
        = static (symbol, solution, ct) => SymbolFinder.FindReferencesAsync(symbol, solution, ct);

    /// <summary>Test seam: number of workspaces with a cached discovery entry. Production code must not read it.</summary>
    internal int CachedWorkspaceCount
    {
        get
        {
            lock (_cacheSync)
            {
                return _cache.Count;
            }
        }
    }

    public TestDiscoveryService(
        IWorkspaceManager workspaceManager,
        ILogger<TestDiscoveryService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        _workspaceManager.WorkspaceClosed += EvictWorkspace;
        // A reload bumps the workspace version, so its entries could never hit again; drop them now.
        _workspaceManager.WorkspaceReloaded += EvictWorkspace;
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= EvictWorkspace;
        _workspaceManager.WorkspaceReloaded -= EvictWorkspace;
    }

    private void EvictWorkspace(string workspaceId)
    {
        lock (_cacheSync)
        {
            _cache.Remove(workspaceId);
        }
    }

    private bool TryGetCached(string workspaceId, int version, out TestDiscoveryDto result)
    {
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(workspaceId, out var cached) && cached.Version == version)
            {
                result = cached.Result;
                return true;
            }
        }

        result = null!;
        return false;
    }

    /// <summary>
    /// Publishes only while the workspace is still open. The workspace manager removes the session
    /// before it raises WorkspaceClosed, and the eviction handler takes the same lock, so either the
    /// handler runs after this publish and removes it, or this check already sees the session gone and
    /// drops it. Only <see cref="IWorkspaceManager.ContainsWorkspace"/> (a lock-free lookup) runs under
    /// the lock.
    /// </summary>
    private void PublishIfWorkspaceLive(string workspaceId, int version, TestDiscoveryDto result)
    {
        lock (_cacheSync)
        {
            // A slower discovery of an older version must not replace a newer entry.
            if (_workspaceManager.ContainsWorkspace(workspaceId)
                && (!_cache.TryGetValue(workspaceId, out var existing) || existing.Version <= version))
            {
                _cache[workspaceId] = (version, result);
            }
        }
    }

    public async Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct)
    {
        var currentVersion = _workspaceManager.GetCurrentVersion(workspaceId);
        if (TryGetCached(workspaceId, currentVersion, out var cachedResult))
            return cachedResult;

        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var testProjectNames = (await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false)).Projects
            .Where(project => project.IsTestProject)
            .Select(project => project.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discoveredProjects = new List<TestProjectDto>();

        foreach (var project in solution.Projects)
        {
            if (!testProjectNames.Contains(project.Name))
            {
                continue;
            }

            try
            {
                var tests = new List<TestCaseDto>();
                foreach (var document in project.Documents)
                {
                    var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    if (root is null)
                    {
                        continue;
                    }

                    foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                    {
                        if (!HasTestAttribute(method))
                        {
                            continue;
                        }

                        var lineSpan = method.Identifier.GetLocation().GetLineSpan();
                        tests.Add(new TestCaseDto(
                            DisplayName: method.Identifier.Text,
                            FullyQualifiedName: BuildFullyQualifiedTestName(project.Name, method),
                            FilePath: document.FilePath,
                            Line: lineSpan.StartLinePosition.Line + 1));
                    }
                }

                discoveredProjects.Add(new TestProjectDto(project.Name, project.FilePath ?? project.Name, tests));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to discover tests in project '{ProjectName}', skipping", project.Name);
            }
        }

        var result = new TestDiscoveryDto(discoveredProjects);
        PublishIfWorkspaceLive(workspaceId, currentVersion, result);
        return result;
    }

    public async Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
        string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct)
    {
        var discovery = await DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);

        IReadOnlyList<(string Project, TestCaseDto Test)> rawMatches = [];
        var referenceSweepCompleted = true;
        if (symbol is not null)
        {
            (rawMatches, referenceSweepCompleted) =
                await CollectRelatedTestsAsync(discovery, solution, symbol, ct).ConfigureAwait(false);
        }

        // Each match carries its owning project (a test's identity is project + FQN), which
        // populates ProjectName on the envelope element type (RelatedTestCaseDto), matching
        // test_related_files's shape.
        var enriched = rawMatches
            .Select(m => new RelatedTestCaseDto(
                DisplayName: m.Test.DisplayName,
                FullyQualifiedName: m.Test.FullyQualifiedName,
                ProjectName: m.Project,
                FilePath: m.Test.FilePath,
                Line: m.Test.Line,
                // Symbol-mode has no source file trigger — the trigger is the symbol itself.
                TriggeredByFiles: []))
            .ToList();

        var total = enriched.Count;
        var page = enriched.Take(maxResults).ToList();
        var dotnetFilter = SynthesizeDotnetTestFilter(page.Select(t => t.FullyQualifiedName));

        // test-related-files-empty-result-explainability: parity with files-mode envelope.
        // Symbol-mode misses break down into "symbol did not resolve" vs "symbol resolved
        // but heuristic + reference sweep matched zero tests".
        var missReasons = new List<string>();
        if (symbol is null)
        {
            missReasons.Add("locator did not resolve to any workspace symbol");
        }
        else if (enriched.Count == 0)
        {
            var searchTerms = string.Join(", ", BuildRelatedTestSearchTerms(symbol).OrderBy(t => t, StringComparer.Ordinal));
            missReasons.Add(
                discovery.TestProjects.Sum(p => p.Tests.Count) == 0
                    ? "symbol resolved but the workspace contains no discovered tests"
                    : $"symbol resolved but no discovered test name/path matched terms [{searchTerms}] and the reference sweep produced no test-file overlaps");
        }
        // Payload-safe: no paths or exception text. The exception itself goes to the logger.
        if (symbol is not null && !referenceSweepCompleted)
        {
            missReasons.Add("reference sweep did not complete for the resolved symbol; results are heuristic-only");
        }
        var heuristicsAttempted = new List<string>();
        if (symbol is not null)
        {
            heuristicsAttempted.Add("symbol-name");
            heuristicsAttempted.Add("container-name");
            if (referenceSweepCompleted)
            {
                heuristicsAttempted.Add("reference-sweep");
            }
        }
        var diagnostics = new RelatedTestsDiagnosticsDto(
            ScannedTestProjects: discovery.TestProjects.Count,
            HeuristicsAttempted: heuristicsAttempted,
            MissReasons: missReasons);

        return new RelatedTestsForSymbolDto(
            page,
            dotnetFilter,
            new PaginationInfo(Total: total, Returned: page.Count, HasMore: total > page.Count),
            diagnostics);
    }

    /// <summary>
    /// Build a <c>dotnet test --filter</c> expression from a sequence of fully-qualified test
    /// names. Shared by <c>test_related</c> and <c>test_related_files</c> so the two tools
    /// emit identically-shaped filter strings.
    /// </summary>
    internal static string SynthesizeDotnetTestFilter(IEnumerable<string> fullyQualifiedNames)
    {
        var filterParts = fullyQualifiedNames
            .Distinct(StringComparer.Ordinal)
            .Select(fqn => $"FullyQualifiedName~{fqn}")
            .ToList();
        return filterParts.Count > 0 ? string.Join("|", filterParts) : string.Empty;
    }

    private static HashSet<string> BuildRelatedTestSearchTerms(ISymbol symbol)
    {
        var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol.Name };
        if (symbol.ContainingType is not null)
        {
            searchTerms.Add(symbol.ContainingType.Name);
        }

        return searchTerms;
    }

    private async Task<(IReadOnlyList<(string Project, TestCaseDto Test)> Matches, bool ReferenceSweepCompleted)> CollectRelatedTestsAsync(
        TestDiscoveryDto discovery,
        Solution solution,
        ISymbol symbol,
        CancellationToken ct)
    {
        var discoveredTests = FlattenDiscoveredTests(discovery);
        var collected = CollectHeuristicRelatedTests(discoveredTests, BuildRelatedTestSearchTerms(symbol));

        var sweepCompleted = await TryAugmentRelatedTestsFromReferencesAsync(
            symbol,
            solution,
            discoveredTests,
            collected,
            ct).ConfigureAwait(false);

        return (collected.Values.ToList(), sweepCompleted);
    }

    private static List<(string Project, TestCaseDto Test)> FlattenDiscoveredTests(TestDiscoveryDto discovery)
    {
        return discovery.TestProjects
            .SelectMany(project => project.Tests.Select(test => (project.ProjectName, test)))
            .ToList();
    }

    /// <summary>
    /// A discovered test's identity: owning project plus fully-qualified name. Two test
    /// projects may declare the same namespace/type/method, so the FQN alone is not unique.
    /// </summary>
    private readonly record struct TestIdentity(string ProjectName, string FullyQualifiedName);

    private static Dictionary<TestIdentity, (string Project, TestCaseDto Test)> CollectHeuristicRelatedTests(
        IReadOnlyList<(string Project, TestCaseDto Test)> discoveredTests,
        IReadOnlySet<string> searchTerms)
    {
        // test-related-empty-for-valid-symbol: name heuristic misses tests that dispatch
        // through an interface (test method references `IService.Method()` but this symbol
        // is the concrete `MyService.Method`). Keep the heuristic as a fast path, then augment
        // it with the reference sweep below for interface-dispatched cases.
        var collected = new Dictionary<TestIdentity, (string Project, TestCaseDto Test)>();
        foreach (var (project, test) in discoveredTests)
        {
            if (searchTerms.Any(term => MatchesRelatedTestTerm(test, term)))
            {
                collected[new TestIdentity(project, test.FullyQualifiedName)] = (project, test);
            }
        }

        return collected;
    }

    private static bool MatchesRelatedTestTerm(TestCaseDto test, string term)
    {
        return test.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               test.FullyQualifiedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               (test.FilePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task<bool> TryAugmentRelatedTestsFromReferencesAsync(
        ISymbol symbol,
        Solution solution,
        IReadOnlyList<(string Project, TestCaseDto Test)> discoveredTests,
        Dictionary<TestIdentity, (string Project, TestCaseDto Test)> collected,
        CancellationToken ct)
    {
        // Reference-based augmentation: collect files that contain a reference to `symbol`
        // (or any override / implementation when the symbol is abstract / interface). Test
        // methods whose file path matches are considered related.
        try
        {
            var referencedFilePaths = await CollectReferencedFilePathsAsync(symbol, solution, ct).ConfigureAwait(false);
            if (referencedFilePaths.Count == 0)
            {
                return true;
            }

            foreach (var (project, test) in discoveredTests)
            {
                if (string.IsNullOrWhiteSpace(test.FilePath))
                {
                    continue;
                }

                if (referencedFilePaths.Contains(Path.GetFullPath(test.FilePath)))
                {
                    collected.TryAdd(new TestIdentity(project, test.FullyQualifiedName), (project, test));
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reference-sweep is best-effort augmentation — never let it break the heuristic path.
            _logger.LogWarning(ex, "FindRelatedTests reference-sweep augmentation failed; returning heuristic results only.");
            return false;
        }
    }

    private async Task<HashSet<string>> CollectReferencedFilePathsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        var referencedFilePaths = new HashSet<string>(FileSystemPath.Comparer);
        var symbolsToWalk = await CollectSymbolAndImplementationsAsync(symbol, solution, ct).ConfigureAwait(false);

        foreach (var candidate in symbolsToWalk)
        {
            ct.ThrowIfCancellationRequested();
            var references = await FindReferencesAsync(candidate, solution, ct).ConfigureAwait(false);
            foreach (var refSet in references)
            {
                foreach (var location in refSet.Locations)
                {
                    AddReferencedFilePath(referencedFilePaths, location.Document.FilePath);
                }
            }
        }

        return referencedFilePaths;
    }

    private static void AddReferencedFilePath(HashSet<string> referencedFilePaths, string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            referencedFilePaths.Add(Path.GetFullPath(filePath));
        }
    }

    /// <summary>
    /// test-related-empty-for-valid-symbol: when the input symbol is an interface or abstract
    /// member, callers often test the concrete implementation(s). Walk the dispatch tree so
    /// the reference-sweep covers interface methods, overrides, and concrete classes alike.
    /// </summary>
    private static async Task<IReadOnlyList<ISymbol>> CollectSymbolAndImplementationsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var results = new List<ISymbol> { symbol };

        if (symbol is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Interface)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            results.AddRange(implementations);
        }
        else if (symbol is IMethodSymbol method && method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
            results.AddRange(implementations);
        }
        else if (symbol is IMethodSymbol concreteMethod && (concreteMethod.IsAbstract || concreteMethod.IsVirtual))
        {
            var overrides = await SymbolFinder.FindOverridesAsync(concreteMethod, solution, cancellationToken: ct).ConfigureAwait(false);
            results.AddRange(overrides);

            // Also walk upward — if this method is an override itself, include the base chain so
            // tests that call the base type's version are still matched.
            var current = concreteMethod;
            while (current.OverriddenMethod is IMethodSymbol overridden)
            {
                results.Add(overridden);
                current = overridden;
            }
        }

        return results;
    }

    public async Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
        string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct)
    {
        if (filePaths.Count > _options.MaxRelatedFiles)
        {
            throw new PublicArgumentException(
                $"A maximum of {_options.MaxRelatedFiles} files may be analyzed at once for related tests.",
                nameof(filePaths));
        }

        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var discovery = await DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
        var allTests = discovery.TestProjects
            .SelectMany(p => p.Tests.Select(t => (Project: p.ProjectName, Test: t)))
            .ToList();

        var testFilePaths = BuildTestFilePathSet(allTests);
        var matchState = await CollectPrimaryFileMatchesAsync(filePaths, solution, allTests, testFilePaths, ct)
            .ConfigureAwait(false);

        // test-related-files-service-refactor-underreporting: when the file-affinity pass
        // produced zero matches across every input file but at least one document resolved,
        // broaden via two 1-hop expansions:
        //   - inbound-reference: walk SymbolFinder.FindReferencesAsync for each declared type
        //     in the resolved file(s); test files containing those references are related.
        //   - namespace-neighbor: collect other types declared in the same namespace as the
        //     input file's types; rerun the name-affinity match against those neighbor type
        //     names.
        // Both run only on the empty-primary-result path so amortized cost stays low.
        // Multi-file service refactors (e.g. MutationAnalysisService + ScaffoldingService,
        // CodePatternAnalyzer + TestDiscoveryService) frequently miss because the test
        // class name doesn't textually contain any input type name; the inbound-reference
        // sweep recovers them by following actual call sites.
        await ApplyFallbackMatchesAsync(matchState, solution, allTests, ct).ConfigureAwait(false);

        // Payload-safe, once per call: a count only, never paths or exception text.
        if (matchState.IncompleteSweepFiles.Count > 0)
        {
            matchState.MissReasons.Add(
                $"related-test discovery sweep did not complete for {matchState.IncompleteSweepFiles.Count} input file(s); results for those files are partial");
        }

        return BuildRelatedTestsForFilesResult(discovery, matchState, maxResults);
    }

    private async Task<RelatedFileMatchState> CollectPrimaryFileMatchesAsync(
        IReadOnlyList<string> filePaths,
        Solution solution,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        HashSet<string> testFilePaths,
        CancellationToken ct)
    {
        var state = new RelatedFileMatchState();

        foreach (var filePath in filePaths)
        {
            var document = FindDocumentByPath(solution, filePath);
            if (document is null)
            {
                state.MissReasons.Add($"path '{filePath}' did not resolve to a workspace document");
                continue;
            }

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null)
            {
                state.MissReasons.Add($"path '{filePath}' resolved to a document with no syntax tree");
                continue;
            }

            state.AnyDocumentResolved = true;
            state.ResolvedDocuments.Add((filePath, document, root));

            await AddDirectReferenceMatchesAsync(
                state,
                filePath,
                document,
                root,
                solution,
                allTests,
                testFilePaths,
                ct).ConfigureAwait(false);

            var searchTerms = BuildFileSearchTerms(filePath, root);
            var perFileMatchCount = AddNameAffinityMatches(
                state,
                filePath,
                searchTerms,
                allTests);

            if (perFileMatchCount == 0)
            {
                var renderedTerms = string.Join(", ", searchTerms.OrderBy(t => t, StringComparer.Ordinal));
                state.MissReasons.Add(
                    allTests.Count == 0
                        ? $"path '{filePath}' resolved but the workspace contains no discovered tests"
                        : $"path '{filePath}' resolved but no discovered test name/path matched any of [{renderedTerms}]");
            }
        }

        return state;
    }

    private static Document? FindDocumentByPath(Solution solution, string filePath)
    {
        var fullFilePath = PhysicalPathResolver.Resolve(filePath);
        return solution
            .Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null &&
                FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(d.FilePath), fullFilePath));
    }

    private async Task AddDirectReferenceMatchesAsync(
        RelatedFileMatchState state,
        string filePath,
        Document document,
        SyntaxNode root,
        Solution solution,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        HashSet<string> testFilePaths,
        CancellationToken ct)
    {
        try
        {
            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return;
            }

            var declaredSymbols = CollectDeclaredReferenceableSymbols(root, semanticModel, ct);
            if (declaredSymbols.Count == 0)
            {
                return;
            }

            foreach (var symbol in declaredSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var referencedTestFiles = await CollectReferencedTestFilesAsync(symbol, solution, testFilePaths, ct)
                    .ConfigureAwait(false);
                foreach (var (project, test) in allTests)
                {
                    if (!string.IsNullOrWhiteSpace(test.FilePath) &&
                        referencedTestFiles.Contains(Path.GetFullPath(test.FilePath)))
                    {
                        AddTrigger(state, new TestIdentity(project, test.FullyQualifiedName), filePath, RelatedTestMatchRank.DirectReference);
                    }
                }
            }

            // Recorded only once the whole sweep for this file finished.
            state.DirectReferenceCompletedFiles++;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Direct-reference ranking is best-effort: keep the name/fallback results, but make
            // the incompleteness visible (log with the exception; payload-safe MissReasons entry).
            _logger.LogWarning(ex, "FindRelatedTestsForFiles direct-reference sweep failed for '{FilePath}'; continuing with name-affinity and fallback matching.", filePath);
            state.DirectReferenceFailed = true;
            state.IncompleteSweepFiles.Add(filePath);
        }
    }

    private const int DirectReferenceSymbolCap = 32;

    private static List<ISymbol> CollectDeclaredReferenceableSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var symbols = new List<ISymbol>();

        foreach (var node in root.DescendantNodes())
        {
            ct.ThrowIfCancellationRequested();
            ISymbol? symbol = node switch
            {
                TypeDeclarationSyntax typeDeclaration => semanticModel.GetDeclaredSymbol(typeDeclaration, ct),
                MethodDeclarationSyntax methodDeclaration => semanticModel.GetDeclaredSymbol(methodDeclaration, ct),
                PropertyDeclarationSyntax propertyDeclaration => semanticModel.GetDeclaredSymbol(propertyDeclaration, ct),
                EventDeclarationSyntax eventDeclaration => semanticModel.GetDeclaredSymbol(eventDeclaration, ct),
                _ => null,
            };

            if (symbol is not null)
            {
                symbols.Add(symbol);
                if (symbols.Count >= DirectReferenceSymbolCap)
                {
                    return symbols;
                }
            }

            if (node is FieldDeclarationSyntax fieldDeclaration)
            {
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable, ct) is IFieldSymbol fieldSymbol)
                    {
                        symbols.Add(fieldSymbol);
                        if (symbols.Count >= DirectReferenceSymbolCap)
                        {
                            return symbols;
                        }
                    }
                }
            }
        }

        return symbols;
    }

    private static HashSet<string> BuildFileSearchTerms(string filePath, SyntaxNode root)
    {
        var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            searchTerms.Add(typeDecl.Identifier.Text);
        }
        searchTerms.Add(Path.GetFileNameWithoutExtension(filePath));
        return searchTerms;
    }

    private static int AddNameAffinityMatches(
        RelatedFileMatchState state,
        string filePath,
        HashSet<string> searchTerms,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests)
    {
        var perFileMatchCount = 0;
        foreach (var (project, test) in allTests)
        {
            if (!TestMatchesAnyTerm(test, searchTerms))
            {
                continue;
            }

            AddTrigger(state, new TestIdentity(project, test.FullyQualifiedName), filePath, RelatedTestMatchRank.NameAffinity);
            perFileMatchCount++;
        }
        return perFileMatchCount;
    }

    private static bool TestMatchesAnyTerm(TestCaseDto test, HashSet<string> searchTerms)
        => searchTerms.Any(term =>
            test.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            test.FullyQualifiedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (test.FilePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));

    private async Task ApplyFallbackMatchesAsync(
        RelatedFileMatchState state,
        Solution solution,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        CancellationToken ct)
    {
        if (state.Matches.Count > 0 || state.ResolvedDocuments.Count == 0 || allTests.Count == 0)
        {
            return;
        }

        var testFilePaths = BuildTestFilePathSet(allTests);
        var (referenceMatches, neighborMatches, fallbackHeuristics, incompleteFiles) =
            await CollectFallbackMatchesAsync(state.ResolvedDocuments, solution, allTests, testFilePaths, ct)
                .ConfigureAwait(false);

        state.FallbackHeuristicsAttempted.AddRange(fallbackHeuristics);
        state.IncompleteSweepFiles.UnionWith(incompleteFiles);

        foreach (var (filePath, project, test) in referenceMatches)
        {
            AddTrigger(state, new TestIdentity(project, test.FullyQualifiedName), filePath, RelatedTestMatchRank.InboundReference);
        }
        foreach (var (filePath, project, test) in neighborMatches)
        {
            AddTrigger(state, new TestIdentity(project, test.FullyQualifiedName), filePath, RelatedTestMatchRank.NamespaceNeighbor);
        }

        if (state.Matches.Count > 0)
        {
            // Broadening recovered tests that the primary heuristic missed — emit a
            // single explanatory miss-reason rather than leaving the per-file
            // "no name matched" reasons as the only signal.
            state.MissReasons.Add(
                $"primary type-name/file-name affinity matched zero tests; fallback broadening (heuristics: [{string.Join(", ", state.FallbackHeuristicsAttempted)}]) recovered {state.Matches.Count} candidate test(s)");
        }
    }

    private static RelatedTestsForFilesDto BuildRelatedTestsForFilesResult(
        TestDiscoveryDto discovery,
        RelatedFileMatchState state,
        int maxResults)
    {
        // Keyed by (project, FQN): equal FQNs in separate test projects are distinct tests.
        // TryAdd: overloads or partial declarations inside ONE project can legitimately share
        // an FQN; they collapse to one identity (first declaration supplies file/line).
        var testCaseLookup = new Dictionary<TestIdentity, TestCaseDto>();
        foreach (var project in discovery.TestProjects)
        {
            foreach (var test in project.Tests)
            {
                testCaseLookup.TryAdd(new TestIdentity(project.ProjectName, test.FullyQualifiedName), test);
            }
        }

        var total = state.Matches.Count;
        var results = state.Matches
            .OrderBy(kv => kv.Value.Rank)
            .ThenBy(kv => kv.Value.Sequence)
            .Take(maxResults)
            .Select(kv =>
            {
                var test = testCaseLookup.TryGetValue(kv.Key, out var t) ? t : null;
                return new RelatedTestCaseDto(
                    DisplayName: test?.DisplayName ?? kv.Key.FullyQualifiedName,
                    FullyQualifiedName: kv.Key.FullyQualifiedName,
                    ProjectName: kv.Key.ProjectName,
                    FilePath: test?.FilePath,
                    Line: test?.Line,
                    TriggeredByFiles: kv.Value.TriggeredByFiles.Distinct().ToList());
            })
            .ToList();

        var dotnetFilter = SynthesizeDotnetTestFilter(results.Select(t => t.FullyQualifiedName));
        var heuristicsAttempted = new List<string>();
        if (state.DirectReferenceCompletedFiles > 0 && !state.DirectReferenceFailed)
        {
            heuristicsAttempted.Add("direct-reference");
        }
        if (state.AnyDocumentResolved)
        {
            heuristicsAttempted.Add("type-name");
            heuristicsAttempted.Add("file-name");
        }
        heuristicsAttempted.AddRange(state.FallbackHeuristicsAttempted);
        var diagnostics = new RelatedTestsDiagnosticsDto(
            ScannedTestProjects: discovery.TestProjects.Count,
            HeuristicsAttempted: heuristicsAttempted,
            MissReasons: state.MissReasons);
        return new RelatedTestsForFilesDto(
            results,
            dotnetFilter,
            new PaginationInfo(Total: total, Returned: results.Count, HasMore: total > results.Count),
            diagnostics);
    }

    private sealed class RelatedFileMatchState
    {
        public Dictionary<TestIdentity, RelatedFileTestMatch> Matches { get; } = new();
        public List<string> MissReasons { get; } = [];
        public bool AnyDocumentResolved { get; set; }
        public int DirectReferenceCompletedFiles { get; set; }
        public bool DirectReferenceFailed { get; set; }
        /// <summary>Input files whose semantic reference sweep (any stage) did not complete. Only the count is surfaced.</summary>
        public HashSet<string> IncompleteSweepFiles { get; } = new(StringComparer.Ordinal);
        public int NextSequence { get; set; }
        public List<(string FilePath, Document Document, SyntaxNode Root)> ResolvedDocuments { get; } = [];
        public List<string> FallbackHeuristicsAttempted { get; } = [];
    }

    private sealed class RelatedFileTestMatch
    {
        public RelatedFileTestMatch(RelatedTestMatchRank rank, int sequence)
        {
            Rank = rank;
            Sequence = sequence;
        }

        public RelatedTestMatchRank Rank { get; set; }
        public int Sequence { get; }
        public List<string> TriggeredByFiles { get; } = [];
    }

    private enum RelatedTestMatchRank
    {
        DirectReference = 0,
        NameAffinity = 1,
        InboundReference = 2,
        NamespaceNeighbor = 3,
    }

    private static void AddTrigger(
        RelatedFileMatchState state,
        TestIdentity identity,
        string filePath,
        RelatedTestMatchRank rank)
    {
        if (!state.Matches.TryGetValue(identity, out var match))
        {
            match = new RelatedFileTestMatch(rank, state.NextSequence++);
            state.Matches[identity] = match;
        }
        else if (rank < match.Rank)
        {
            match.Rank = rank;
        }

        if (!match.TriggeredByFiles.Contains(filePath, FileSystemPath.Comparer))
        {
            match.TriggeredByFiles.Add(filePath);
        }
    }

    private static HashSet<string> BuildTestFilePathSet(
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests)
    {
        var set = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var (_, test) in allTests)
        {
            if (!string.IsNullOrWhiteSpace(test.FilePath))
            {
                set.Add(Path.GetFullPath(test.FilePath));
            }
        }
        return set;
    }

    /// <summary>
    /// test-related-files-service-refactor-underreporting: fallback broadening when the
    /// primary type-name/file-name heuristic produced zero matches. Runs two 1-hop
    /// expansions in parallel:
    /// <list type="number">
    ///   <item>
    ///     <b>inbound-reference</b> — for each type declared in a resolved input file,
    ///     <see cref="SymbolFinder.FindReferencesAsync"/> locates every call site; tests
    ///     whose source file path appears in those references are recovered.
    ///   </item>
    ///   <item>
    ///     <b>namespace-neighbor</b> — for each type declared in a resolved input file,
    ///     other types declared in the same namespace (across the project) are added to
    ///     the search-term set. The name-affinity match reruns over the test surface using
    ///     those neighbor type names. Capped at one hop — neighbors of neighbors are not
    ///     walked, to bound noise.
    ///   </item>
    /// </list>
    /// Results from both expansions are de-duplicated by the caller via <c>AddTrigger</c>.
    /// </summary>
    private async Task<(
        List<(string FilePath, string Project, TestCaseDto Test)> ReferenceMatches,
        List<(string FilePath, string Project, TestCaseDto Test)> NeighborMatches,
        List<string> Heuristics,
        HashSet<string> IncompleteFiles)> CollectFallbackMatchesAsync(
            IReadOnlyList<(string FilePath, Document Document, SyntaxNode Root)> resolvedDocuments,
            Solution solution,
            IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
            HashSet<string> testFilePaths,
            CancellationToken ct)
    {
        var referenceMatches = new List<(string FilePath, string Project, TestCaseDto Test)>();
        var neighborMatches = new List<(string FilePath, string Project, TestCaseDto Test)>();
        var heuristics = new List<string>();

        var referenceCompleted = false;
        var neighborCompleted = false;
        var referenceFailed = false;
        var neighborFailed = false;
        var incompleteFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (filePath, document, root) in resolvedDocuments)
        {
            ct.ThrowIfCancellationRequested();

            var (semanticModel, modelFailed) = await TryGetSemanticModelAsync(document, filePath, ct).ConfigureAwait(false);
            if (modelFailed)
            {
                // Neither fallback stage could run for this file.
                referenceFailed = true;
                neighborFailed = true;
                incompleteFiles.Add(filePath);
                continue;
            }
            if (semanticModel is null)
            {
                continue;
            }

            var declaredTypeSymbols = CollectDeclaredTypeSymbols(root, semanticModel, ct);
            if (declaredTypeSymbols.Count == 0)
            {
                continue;
            }

            // ── inbound-reference expansion ────────────────────────────────────────────
            if (await AddInboundReferenceMatchesAsync(
                filePath,
                declaredTypeSymbols,
                solution,
                allTests,
                testFilePaths,
                referenceMatches,
                ct).ConfigureAwait(false))
            {
                referenceCompleted = true;
            }
            else
            {
                referenceFailed = true;
                incompleteFiles.Add(filePath);
            }

            // ── namespace-neighbor expansion (1 hop) ───────────────────────────────────
            if (AddNamespaceNeighborMatches(filePath, declaredTypeSymbols, document.Project, allTests, neighborMatches))
            {
                neighborCompleted = true;
            }
            else
            {
                neighborFailed = true;
                incompleteFiles.Add(filePath);
            }
        }

        // A heuristic is listed only when it completed for every file it was attempted on.
        if (referenceCompleted && !referenceFailed)
        {
            heuristics.Add("inbound-reference");
        }
        if (neighborCompleted && !neighborFailed)
        {
            heuristics.Add("namespace-neighbor");
        }

        return (referenceMatches, neighborMatches, heuristics, incompleteFiles);
    }

    private async Task<(SemanticModel? Model, bool Failed)> TryGetSemanticModelAsync(Document document, string filePath, CancellationToken ct)
    {
        try
        {
            return (await document.GetSemanticModelAsync(ct).ConfigureAwait(false), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FindRelatedTestsForFiles fallback: failed to obtain semantic model for '{FilePath}', skipping symbolic broadening for this file.", filePath);
            return (null, true);
        }
    }

    private static List<INamedTypeSymbol> CollectDeclaredTypeSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var declaredTypeSymbols = new List<INamedTypeSymbol>();
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDecl, ct) is INamedTypeSymbol typeSymbol)
            {
                declaredTypeSymbols.Add(typeSymbol);
            }
        }
        return declaredTypeSymbols;
    }

    private async Task<bool> AddInboundReferenceMatchesAsync(
        string filePath,
        IReadOnlyList<INamedTypeSymbol> declaredTypeSymbols,
        Solution solution,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        HashSet<string> testFilePaths,
        List<(string FilePath, string Project, TestCaseDto Test)> referenceMatches,
        CancellationToken ct)
    {
        try
        {
            foreach (var typeSymbol in declaredTypeSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var referencedTestFiles = await CollectReferencedTestFilesAsync(typeSymbol, solution, testFilePaths, ct)
                    .ConfigureAwait(false);
                AddTestsFromReferencedFiles(filePath, allTests, referencedTestFiles, referenceMatches);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reference sweep is best-effort. Never let it break the heuristic path.
            _logger.LogWarning(ex, "FindRelatedTestsForFiles fallback inbound-reference sweep failed for '{FilePath}'; continuing with namespace-neighbor expansion.", filePath);
            return false;
        }
    }

    private async Task<HashSet<string>> CollectReferencedTestFilesAsync(
        ISymbol symbol,
        Solution solution,
        HashSet<string> testFilePaths,
        CancellationToken ct)
    {
        var referencedTestFiles = new HashSet<string>(FileSystemPath.Comparer);
        var references = await FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        foreach (var refSet in references)
        {
            foreach (var location in refSet.Locations)
            {
                var refFilePath = location.Document?.FilePath;
                if (string.IsNullOrWhiteSpace(refFilePath))
                {
                    continue;
                }
                var fullPath = Path.GetFullPath(refFilePath);
                if (testFilePaths.Contains(fullPath))
                {
                    referencedTestFiles.Add(fullPath);
                }
            }
        }
        return referencedTestFiles;
    }

    private static void AddTestsFromReferencedFiles(
        string filePath,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        HashSet<string> referencedTestFiles,
        List<(string FilePath, string Project, TestCaseDto Test)> referenceMatches)
    {
        foreach (var (project, test) in allTests)
        {
            if (!string.IsNullOrWhiteSpace(test.FilePath) &&
                referencedTestFiles.Contains(Path.GetFullPath(test.FilePath)))
            {
                referenceMatches.Add((filePath, project, test));
            }
        }
    }

    private bool AddNamespaceNeighborMatches(
        string filePath,
        IReadOnlyList<INamedTypeSymbol> declaredTypeSymbols,
        Project project,
        IReadOnlyList<(string Project, TestCaseDto Test)> allTests,
        List<(string FilePath, string Project, TestCaseDto Test)> neighborMatches)
    {
        try
        {
            var neighborTerms = CollectNamespaceNeighborTerms(declaredTypeSymbols, project);
            foreach (var (testProject, test) in allTests)
            {
                if (TestMatchesAnyTerm(test, neighborTerms))
                {
                    neighborMatches.Add((filePath, testProject, test));
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FindRelatedTestsForFiles fallback namespace-neighbor expansion failed for '{FilePath}'; continuing.", filePath);
            return false;
        }
    }

    /// <summary>
    /// Collect unqualified type names declared in the same namespace as <paramref name="declaredTypes"/>,
    /// limited to the same project (1-hop). Excludes the input types themselves so the caller
    /// can search the test surface for distinct neighbor names. The intent is to recover tests
    /// for cohesive service refactors — when <c>FooService</c> ships alongside <c>BarService</c>
    /// in the same namespace, a change touching only <c>FooService</c> should still surface
    /// tests that exercise <c>BarService</c> if they live next door.
    /// </summary>
    private static HashSet<string> CollectNamespaceNeighborTerms(
        IReadOnlyList<INamedTypeSymbol> declaredTypes,
        Project project)
    {
        var inputTypeNames = new HashSet<string>(declaredTypes.Select(t => t.Name), StringComparer.Ordinal);
        var inputNamespaceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeSymbol in declaredTypes)
        {
            var ns = typeSymbol.ContainingNamespace;
            if (ns is not null && !ns.IsGlobalNamespace)
            {
                inputNamespaceNames.Add(ns.ToDisplayString());
            }
        }

        if (inputNamespaceNames.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var neighborTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectAssembly = project.TryGetCompilation(out var compilation)
            ? compilation?.Assembly
            : null;

        if (projectAssembly is not null)
        {
            CollectMatchingTypeNamesFromNamespace(projectAssembly.GlobalNamespace, inputNamespaceNames, inputTypeNames, neighborTerms);
        }

        return neighborTerms;
    }

    private static void CollectMatchingTypeNamesFromNamespace(
        INamespaceSymbol namespaceSymbol,
        IReadOnlySet<string> targetNamespaces,
        IReadOnlySet<string> exclude,
        HashSet<string> neighborTerms)
    {
        var fullName = namespaceSymbol.IsGlobalNamespace ? string.Empty : namespaceSymbol.ToDisplayString();
        if (!namespaceSymbol.IsGlobalNamespace && targetNamespaces.Contains(fullName))
        {
            foreach (var typeMember in namespaceSymbol.GetTypeMembers())
            {
                if (!exclude.Contains(typeMember.Name))
                {
                    neighborTerms.Add(typeMember.Name);
                }
            }
        }

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            CollectMatchingTypeNamesFromNamespace(nestedNamespace, targetNamespaces, exclude, neighborTerms);
        }
    }

    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        // MSTest
        "TestMethod", "TestMethodAttribute", "DataTestMethod", "DataTestMethodAttribute",
        // xUnit
        "Fact", "FactAttribute", "Theory", "TheoryAttribute",
        // NUnit
        "Test", "TestAttribute", "TestCase", "TestCaseAttribute", "TestCaseSource", "TestCaseSourceAttribute",
    };

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();
                // Handle both short (Fact) and qualified (Xunit.FactAttribute) forms
                var simpleName = attributeName.Contains('.') ? attributeName[(attributeName.LastIndexOf('.') + 1)..] : attributeName;
                if (TestAttributeNames.Contains(simpleName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// test-run-fqdn-drift-vs-test-discover: build the fully-qualified test name using the
    /// method's actual declared namespace (read from the syntax tree) rather than the
    /// MSBuild project name. Test runners (xunit / MSTest / NUnit / TUnit) register tests under
    /// <c>{declared-namespace}.{containing-type}.{method}</c>; nested types use the runtime-style
    /// <c>Outer+Inner</c> separator. Using the project name as the
    /// prefix produced silent zero-hits when a test class lived in a sub-namespace (e.g.
    /// <c>SampleLib.Tests.Unit.AnimalServiceTests</c> in a project named
    /// <c>SampleLib.Tests</c>) because <c>--filter "FullyQualifiedName~..."</c> never matched.
    /// Falls back to <paramref name="projectName"/> when no enclosing namespace is found
    /// (test class declared in the global namespace).
    /// </summary>
    internal static string BuildFullyQualifiedTestName(string projectName, MethodDeclarationSyntax method)
    {
        var containingTypes = method.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.Text)
            .ToList();
        var containingType = containingTypes.Count == 0
            ? "Unknown"
            : string.Join('+', containingTypes);
        var namespacePrefix = GetEnclosingNamespace(method) ?? projectName;
        return $"{namespacePrefix}.{containingType}.{method.Identifier.Text}";
    }

    /// <summary>
    /// Walks ancestors of <paramref name="node"/> to find every enclosing
    /// <see cref="BaseNamespaceDeclarationSyntax"/> (covers both classic
    /// <see cref="NamespaceDeclarationSyntax"/> and
    /// <see cref="FileScopedNamespaceDeclarationSyntax"/>) and concatenates their names
    /// outer-to-inner. Returns <see langword="null"/> when no enclosing namespace exists
    /// (declaration in the global namespace).
    /// </summary>
    internal static string? GetEnclosingNamespace(SyntaxNode node)
    {
        var parts = new List<string>();
        // Walk from the node upward; collect every namespace declaration so nested namespace
        // declarations like `namespace A { namespace B { class C { } } }` produce "A.B".
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                parts.Add(ns.Name.ToString());
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        // Ancestor walk produces inner-to-outer; reverse for outer-to-inner concatenation.
        parts.Reverse();
        return string.Join(".", parts);
    }
}
