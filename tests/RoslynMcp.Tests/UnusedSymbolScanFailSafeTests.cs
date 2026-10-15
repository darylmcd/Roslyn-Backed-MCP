using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the unused-symbol-scan-fail-unsafe-reference-count fix:
/// <see cref="UnusedCodeAnalyzer"/>'s per-candidate reference scan (used by both
/// <c>find_unused_symbols</c> and <c>remove_dead_code_preview</c>'s independent
/// removal guard) must never treat a failed reference-count verification as a
/// confident zero. (a)/(b) pin the pre-existing correct-classification behavior
/// as a baseline; (c)/(d) exercise the new fail-closed behavior via the internal
/// injectable reference-finder seam.
/// </summary>
[TestClass]
public sealed class UnusedSymbolScanFailSafeTests
{
    private readonly List<UnusedCodeAnalyzerTestHarness> _harnesses = [];

    [TestCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await CleanupFailureCollector.RunAsync(
                "Failed to dispose analyzer fixtures.",
                _harnesses.Select(harness => CleanupFailureCollector.FromAction(harness.Dispose)).ToArray());
        }
        finally
        {
            _harnesses.Clear();
        }
    }

    private const string WorkspaceId = "unused-scan-fail-safe-test-ws";

    [TestMethod]
    public async Task FindUnusedSymbols_PrivateStaticMethodInvokedEarlierInFile_IsNeverReportedUnused()
    {
        const string source = """
            namespace Sample;
            internal sealed class Runner
            {
                public void Run() => Helper();

                private static void Helper()
                {
                }
            }
            """;

        var (analyzer, _, _) = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindUnusedSymbolsAsync(
            WorkspaceId,
            new UnusedSymbolsAnalysisOptions { IncludePublic = true },
            default);

        Assert.IsFalse(
            hits.Any(hit => hit.SymbolName == "Helper"),
            "A private static method invoked earlier in the same file must not be reported unused.");
    }

    [TestMethod]
    public async Task FindDeadFields_PrivateReadonlyFieldOfNestedStructReadInFile_IsNeverReportedUnused()
    {
        const string source = """
            namespace Sample;
            internal sealed class Container
            {
                private struct Info
                {
                    private readonly int _value;

                    public Info(int value)
                    {
                        _value = value;
                    }

                    public int Get() => _value;
                }
            }
            """;

        var (analyzer, _, _) = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.IsFalse(
            hits.Any(hit => hit.SymbolName == "_value"),
            "A private readonly field of a nested struct that is read in the same file must not be reported unused.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_ReferenceFinderThrowsForOneCandidate_ThrowsInsteadOfSilentlyOmitting()
    {
        const string source = """
            namespace Sample;
            internal sealed class Unreferenced
            {
                private static void Foo()
                {
                }

                private static void Bar()
                {
                }
            }
            """;

        var exceptionReporter = new RecordingUnexpectedExceptionReporter();
        var (analyzer, _, _) = BuildAnalyzerWithSource(
            source,
            referenceFinder: (symbol, solution, ct) => symbol.Name == "Bar"
                ? throw new InvalidOperationException("simulated reference-scan failure")
                : SymbolFinder.FindReferencesAsync(symbol, solution, ct),
            exceptionReporter: exceptionReporter);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            analyzer.FindUnusedSymbolsAsync(
                WorkspaceId,
                new UnusedSymbolsAnalysisOptions(),
                default));

        StringAssert.Contains(ex.Message, "1 candidate");
        Assert.AreEqual(1, exceptionReporter.ReportCount);
        Assert.AreEqual(UnexpectedExceptionCategory.AnalysisScan, exceptionReporter.Categories.Single());
        // The MCP tool boundary (ToolErrorHandler) surfaces PublicMessage verbatim for a
        // PublicInvalidOperationException instead of its generic InvalidOperationException
        // fallback — pin that the scan-failure text actually reaches the caller, not just the
        // raw C# exception.
        StringAssert.Contains(ex.PublicMessage, "1 candidate");
    }

    [TestMethod]
    public async Task PreviewRemoveDeadCode_ReferenceFinderThrows_RefusesRemovalInsteadOfProceeding()
    {
        const string source = """
            namespace Sample;
            internal sealed class Unreferenced
            {
                private static void Foo()
                {
                }
            }
            """;

        var (analyzer, workspaceManager, previewStore) = BuildAnalyzerWithSource(
            source,
            referenceFinder: (_, _, _) => throw new InvalidOperationException("simulated reference-scan failure"));

        var solution = workspaceManager.GetCurrentSolution(WorkspaceId);
        var fooSymbol = await FindMethodSymbolAsync(solution, "Foo");
        var handle = SymbolHandleSerializer.CreateHandle(fooSymbol);

        var deadCodeService = new DeadCodeService(
            workspaceManager,
            previewStore,
            (_, _, _) => throw new InvalidOperationException("simulated reference-scan failure"));

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            deadCodeService.PreviewRemoveDeadCodeAsync(
                WorkspaceId,
                new DeadCodeRemovalDto([handle]),
                default));

        StringAssert.Contains(ex.Message, "Cannot verify");
        StringAssert.Contains(ex.Message, "refusing removal");
        // Same MCP-boundary pin as the scan-failure test above: PublicMessage is what
        // ToolErrorHandler actually surfaces to the remove_dead_code_preview caller.
        StringAssert.Contains(ex.PublicMessage, "Cannot verify");

        // The UnusedCodeAnalyzer built above is not exercised by this DeadCodeService-focused
        // test; discard it so the shared BuildAnalyzerWithSource helper stays reusable across
        // all four tests in this file without an unused-variable warning.
        _ = analyzer;
    }

    private static async Task<ISymbol> FindMethodSymbolAsync(Solution solution, string methodName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var root = await tree.GetRootAsync().ConfigureAwait(false);
                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var methodDecl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    if (symbol is not null && symbol.Name == methodName)
                    {
                        return symbol;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' was not found in the test solution.");
    }

    private (UnusedCodeAnalyzer Analyzer, FailClosedWorkspaceManagerStub WorkspaceManager, PreviewStore PreviewStore) BuildAnalyzerWithSource(
        string source,
        Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>>? referenceFinder = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        var workspace = UnusedCodeAnalyzerTestHarness.CreateWorkspace(source,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var harness = new UnusedCodeAnalyzerTestHarness(WorkspaceId, workspace, referenceFinder, exceptionReporter);
        _harnesses.Add(harness);
        return (harness.Analyzer, harness.WorkspaceManager, new PreviewStore());
    }

    private sealed class RecordingUnexpectedExceptionReporter : IUnexpectedExceptionReporter
    {
        public int ReportCount { get; private set; }

        public List<UnexpectedExceptionCategory> Categories { get; } = [];

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            ReportCount++;
            Categories.Add(category);
            return PublicExceptionDetailPolicy.ProjectUnexpected(exception, $"unused-scan-test-{ReportCount}");
        }
    }
}
