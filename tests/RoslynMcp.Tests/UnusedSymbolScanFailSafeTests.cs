using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

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

        var (analyzer, _, _) = BuildAnalyzerWithSource(
            source,
            referenceFinder: (symbol, solution, ct) => symbol.Name == "Bar"
                ? throw new InvalidOperationException("simulated reference-scan failure")
                : SymbolFinder.FindReferencesAsync(symbol, solution, ct));

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            analyzer.FindUnusedSymbolsAsync(
                WorkspaceId,
                new UnusedSymbolsAnalysisOptions(),
                default));

        StringAssert.Contains(ex.Message, "1 candidate");
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

    private static (UnusedCodeAnalyzer Analyzer, TestWorkspaceManager WorkspaceManager, PreviewStore PreviewStore) BuildAnalyzerWithSource(
        string source,
        Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>>? referenceFinder = null)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestAsm",
            assemblyName: "TestAsm",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "TestAsm.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ]);
        workspace.AddProject(projectInfo);

        var docId = DocumentId.CreateNewId(projectId);
        var fileName = "Sample.cs";
        var fullPath = Path.Combine(Path.GetTempPath(), fileName);
        workspace.AddDocument(DocumentInfo.Create(
            docId,
            fileName,
            filePath: fullPath,
            loader: TextLoader.From(
                TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create(),
                    fullPath))));

        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        var cache = new CompilationCache(wsManager);
        var previewStore = new PreviewStore();

        var analyzer = referenceFinder is null
            ? new UnusedCodeAnalyzer(wsManager, cache, NullLogger<UnusedCodeAnalyzer>.Instance)
            : new UnusedCodeAnalyzer(wsManager, cache, NullLogger<UnusedCodeAnalyzer>.Instance, referenceFinder);

        return (analyzer, wsManager, previewStore);
    }

    private sealed class TestWorkspaceManager : IWorkspaceManager
    {
        private readonly string _workspaceId;
        private readonly AdhocWorkspace _workspace;

        public event Action<string>? WorkspaceClosed;
        public event Action<string>? WorkspaceReloaded;

        public TestWorkspaceManager(string workspaceId, AdhocWorkspace workspace)
        {
            _workspaceId = workspaceId;
            _workspace = workspace;
        }

        public void RaiseWorkspaceClosed(string workspaceId) => WorkspaceClosed?.Invoke(workspaceId);
        public void RaiseWorkspaceReloaded(string workspaceId) => WorkspaceReloaded?.Invoke(workspaceId);

        public Solution GetCurrentSolution(string workspaceId)
        {
            return workspaceId == _workspaceId
                ? _workspace.CurrentSolution
                : throw new InvalidOperationException($"Unknown workspace {workspaceId}");
        }

        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public bool ContainsWorkspace(string workspaceId) => workspaceId == _workspaceId;
        public bool IsStale(string workspaceId) => false;
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
    }
}
