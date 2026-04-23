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

    public async Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var discovery = await DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        return symbol is null
            ? []
            : await CollectRelatedTestsAsync(discovery, solution, symbol, ct).ConfigureAwait(false);
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

        foreach (var filePath in filePaths)
        {
            var document = solution
                .Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath is not null &&
                    string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));

            if (document is null) continue;

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null) continue;

            var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect type names declared in the file from syntax (no semantic model needed)
            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                searchTerms.Add(typeDecl.Identifier.Text);
            }

            // Also use the file name as a search term
            searchTerms.Add(Path.GetFileNameWithoutExtension(filePath));

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
            }
        }

        var projectLookup = discovery.TestProjects
            .SelectMany(p => p.Tests.Select(t => (t.FullyQualifiedName, p.ProjectName)))
            .ToDictionary(x => x.FullyQualifiedName, x => x.ProjectName, StringComparer.Ordinal);

        var testCaseLookup = discovery.TestProjects
            .SelectMany(p => p.Tests)
            .ToDictionary(t => t.FullyQualifiedName, StringComparer.Ordinal);

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

        var filterParts = results
            .Select(t => t.FullyQualifiedName)
            .Distinct()
            .Select(fqn => $"FullyQualifiedName~{fqn}")
            .ToList();
        var dotnetFilter = filterParts.Count > 0 ? string.Join("|", filterParts) : string.Empty;

        return new RelatedTestsForFilesDto(results, dotnetFilter);
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
