using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RoslynMcp.Roslyn.Services;

public sealed class TestDiscoveryService : ITestDiscoveryService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<TestDiscoveryService> _logger;
    private readonly ValidationServiceOptions _options;
    private readonly ConcurrentDictionary<string, (int Version, TestDiscoveryDto Result)> _cache = new();

    public TestDiscoveryService(
        IWorkspaceManager workspaceManager,
        ILogger<TestDiscoveryService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
    }

    public async Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct)
    {
        var currentVersion = _workspaceManager.GetCurrentVersion(workspaceId);
        if (_cache.TryGetValue(workspaceId, out var cached) && cached.Version == currentVersion)
            return cached.Result;

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
                        var containingType = method.Parent switch
                        {
                            ClassDeclarationSyntax cls => cls.Identifier.Text,
                            _ => "Unknown"
                        };
                        tests.Add(new TestCaseDto(
                            DisplayName: method.Identifier.Text,
                            FullyQualifiedName: $"{project.Name}.{containingType}.{method.Identifier.Text}",
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
        _cache[workspaceId] = (currentVersion, result);
        return result;
    }

    public async Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
        string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct)
    {
        var discovery = await DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);

        IReadOnlyList<TestCaseDto> rawMatches = symbol is null
            ? []
            : await CollectRelatedTestsAsync(discovery, solution, symbol, ct).ConfigureAwait(false);

        // Project lookup so we can populate ProjectName on the envelope element type
        // (RelatedTestCaseDto), matching test_related_files's shape.
        var projectLookup = discovery.TestProjects
            .SelectMany(p => p.Tests.Select(t => (t.FullyQualifiedName, p.ProjectName)))
            .GroupBy(x => x.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ProjectName, StringComparer.Ordinal);

        var enriched = rawMatches
            .Select(t => new RelatedTestCaseDto(
                DisplayName: t.DisplayName,
                FullyQualifiedName: t.FullyQualifiedName,
                ProjectName: projectLookup.TryGetValue(t.FullyQualifiedName, out var p) ? p : string.Empty,
                FilePath: t.FilePath,
                Line: t.Line,
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
        var heuristicsAttempted = symbol is null
            ? (IReadOnlyList<string>)[]
            : ["symbol-name", "container-name", "reference-sweep"];
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

    private async Task<IReadOnlyList<TestCaseDto>> CollectRelatedTestsAsync(
        TestDiscoveryDto discovery,
        Solution solution,
        ISymbol symbol,
        CancellationToken ct)
    {
        var discoveredTests = FlattenDiscoveredTests(discovery);
        var collected = CollectHeuristicRelatedTests(discoveredTests, BuildRelatedTestSearchTerms(symbol));

        await TryAugmentRelatedTestsFromReferencesAsync(
            symbol,
            solution,
            discoveredTests,
            collected,
            ct).ConfigureAwait(false);

        return collected.Values.ToList();
    }

    private static List<TestCaseDto> FlattenDiscoveredTests(TestDiscoveryDto discovery)
    {
        return discovery.TestProjects.SelectMany(project => project.Tests).ToList();
    }

    private static Dictionary<string, TestCaseDto> CollectHeuristicRelatedTests(
        IReadOnlyList<TestCaseDto> discoveredTests,
        IReadOnlySet<string> searchTerms)
    {
        // test-related-empty-for-valid-symbol: name heuristic misses tests that dispatch
        // through an interface (test method references `IService.Method()` but this symbol
        // is the concrete `MyService.Method`). Keep the heuristic as a fast path, then augment
        // it with the reference sweep below for interface-dispatched cases.
        var collected = new Dictionary<string, TestCaseDto>(StringComparer.Ordinal);
        foreach (var test in discoveredTests)
        {
            if (searchTerms.Any(term => MatchesRelatedTestTerm(test, term)))
            {
                collected[test.FullyQualifiedName] = test;
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

    private async Task TryAugmentRelatedTestsFromReferencesAsync(
        ISymbol symbol,
        Solution solution,
        IReadOnlyList<TestCaseDto> discoveredTests,
        Dictionary<string, TestCaseDto> collected,
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
                return;
            }

            foreach (var test in discoveredTests)
            {
                if (string.IsNullOrWhiteSpace(test.FilePath))
                {
                    continue;
                }

                if (referencedFilePaths.Contains(Path.GetFullPath(test.FilePath)))
                {
                    collected.TryAdd(test.FullyQualifiedName, test);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reference-sweep is best-effort augmentation — never let it break the heuristic path.
            _logger.LogWarning(ex, "FindRelatedTests reference-sweep augmentation failed; returning heuristic results only.");
        }
    }

    private static async Task<HashSet<string>> CollectReferencedFilePathsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        var referencedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var symbolsToWalk = await CollectSymbolAndImplementationsAsync(symbol, solution, ct).ConfigureAwait(false);

        foreach (var candidate in symbolsToWalk)
        {
            ct.ThrowIfCancellationRequested();
            var references = await SymbolFinder.FindReferencesAsync(candidate, solution, ct).ConfigureAwait(false);
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
            throw new ArgumentException(
                $"A maximum of {_options.MaxRelatedFiles} files may be analyzed at once for related tests.",
                nameof(filePaths));
        }

        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var discovery = await DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
        var allTests = discovery.TestProjects
            .SelectMany(p => p.Tests.Select(t => (Project: p.ProjectName, Test: t)))
            .ToList();

        var testToTriggers = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // test-related-files-empty-result-explainability: track per-file outcomes so an empty
        // Tests list can be explained — "path did not resolve to a workspace document",
        // "no type/file-name term matched", etc. — instead of leaving the caller to guess.
        var filesThatMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missReasons = new List<string>();
        var anyDocumentResolved = false;

        foreach (var filePath in filePaths)
        {
            var document = solution
                .Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath is not null &&
                    string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));

            if (document is null)
            {
                missReasons.Add($"path '{filePath}' did not resolve to a workspace document");
                continue;
            }

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null)
            {
                missReasons.Add($"path '{filePath}' resolved to a document with no syntax tree");
                continue;
            }

            anyDocumentResolved = true;

            var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect type names declared in the file from syntax (no semantic model needed)
            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                searchTerms.Add(typeDecl.Identifier.Text);
            }

            // Also use the file name as a search term
            searchTerms.Add(Path.GetFileNameWithoutExtension(filePath));

            var perFileMatchCount = 0;
            foreach (var (projectName, test) in allTests)
            {
                if (!searchTerms.Any(term =>
                    test.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    test.FullyQualifiedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (test.FilePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
                    continue;

                var key = test.FullyQualifiedName;
                if (!testToTriggers.TryGetValue(key, out var triggers))
                {
                    triggers = new List<string>();
                    testToTriggers[key] = triggers;
                }
                triggers.Add(filePath);
                perFileMatchCount++;
            }

            if (perFileMatchCount == 0)
            {
                var renderedTerms = string.Join(", ", searchTerms.OrderBy(t => t, StringComparer.Ordinal));
                missReasons.Add(
                    allTests.Count == 0
                        ? $"path '{filePath}' resolved but the workspace contains no discovered tests"
                        : $"path '{filePath}' resolved but no discovered test name/path matched any of [{renderedTerms}]");
            }
            else
            {
                filesThatMatched.Add(filePath);
            }
        }

        var projectLookup = discovery.TestProjects
            .SelectMany(p => p.Tests.Select(t => (t.FullyQualifiedName, p.ProjectName)))
            .ToDictionary(x => x.FullyQualifiedName, x => x.ProjectName, StringComparer.Ordinal);

        var testCaseLookup = discovery.TestProjects
            .SelectMany(p => p.Tests)
            .ToDictionary(t => t.FullyQualifiedName, StringComparer.Ordinal);

        var total = testToTriggers.Count;
        var results = testToTriggers
            .Take(maxResults)
            .Select(kv =>
            {
                var test = testCaseLookup.TryGetValue(kv.Key, out var t) ? t : null;
                var projectName = projectLookup.TryGetValue(kv.Key, out var pn) ? pn : string.Empty;
                return new RelatedTestCaseDto(
                    DisplayName: test?.DisplayName ?? kv.Key,
                    FullyQualifiedName: kv.Key,
                    ProjectName: projectName,
                    FilePath: test?.FilePath,
                    Line: test?.Line,
                    TriggeredByFiles: kv.Value.Distinct().ToList());
            })
            .ToList();

        var dotnetFilter = SynthesizeDotnetTestFilter(results.Select(t => t.FullyQualifiedName));
        var heuristicsAttempted = anyDocumentResolved
            ? (IReadOnlyList<string>)["type-name", "file-name"]
            : [];
        var diagnostics = new RelatedTestsDiagnosticsDto(
            ScannedTestProjects: discovery.TestProjects.Count,
            HeuristicsAttempted: heuristicsAttempted,
            MissReasons: missReasons);
        return new RelatedTestsForFilesDto(
            results,
            dotnetFilter,
            new PaginationInfo(Total: total, Returned: results.Count, HasMore: total > results.Count),
            diagnostics);
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
}
