using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="UnusedCodeAnalyzer.FindDeadFieldsAsync"/>. Uses an
/// in-memory <see cref="AdhocWorkspace"/> so the detector can classify source-declared
/// fields as never-read, never-written, or never-either without relying on the sample
/// solution's incidental shapes.
/// </summary>
[TestClass]
public sealed class DeadFieldDetectorTests
{
    private const string WorkspaceId = "dead-field-test-ws";

    [TestMethod]
    public async Task FindDeadFields_FieldWithInitializerAndNoReads_IsNeverRead()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count = 5;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-read", hits[0].UsageKind);
        Assert.AreEqual(0, hits[0].ReadReferenceCount);
        Assert.AreEqual(1, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldReadButNeverWritten_IsNeverWritten()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;
                public int Read() => _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-written", hits[0].UsageKind);
        Assert.AreEqual(1, hits[0].ReadReferenceCount);
        Assert.AreEqual(0, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldWithoutReferencesOrInitializer_IsNeverEither()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-either", hits[0].UsageKind);
        Assert.AreEqual(0, hits[0].ReadReferenceCount);
        Assert.AreEqual(0, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldWrittenAndRead_IsNotFlagged()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;

                public void Set(int value)
                {
                    _count = value;
                }

                public int Read() => _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public async Task FindDeadFields_RefArgumentCountsAsReadWrite()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;

                public void Touch()
                {
                    Bump(ref _count);
                }

                private static void Bump(ref int value)
                {
                    value++;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Passing a field by ref must count as both a read and a write.");
    }

    [TestMethod]
    public async Task FindDeadFields_DefaultExcludesPublicFields_AndUsageKindFilterWorks()
    {
        const string source = """
            namespace Sample;
            public sealed class Counter
            {
                public int PublicField = 1;
                private int _neverRead = 1;
                private int _neverWritten;

                public int Read() => _neverWritten;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var defaultHits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        CollectionAssert.AreEquivalent(
            new[] { "_neverRead", "_neverWritten" },
            defaultHits.Select(hit => hit.SymbolName).OrderBy(name => name).ToArray());

        var filteredHits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions
            {
                IncludePublic = true,
                UsageKindFilter = "never-read"
            },
            default);

        CollectionAssert.AreEquivalent(
            new[] { "PublicField", "_neverRead" },
            filteredHits.Select(hit => hit.SymbolName).OrderBy(name => name).ToArray());
        Assert.IsTrue(filteredHits.All(hit => hit.UsageKind == "never-read"));
    }

    [TestMethod]
    public async Task FindDeadFields_LimitCapsResults()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _a;
                private int _b;
                private int _c;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions { Limit = 2 },
            default);

        Assert.AreEqual(2, hits.Count);
    }

    private static UnusedCodeAnalyzer BuildAnalyzerWithSource(string source)
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
        return new UnusedCodeAnalyzer(
            wsManager,
            cache,
            NullLogger<UnusedCodeAnalyzer>.Instance);
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

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
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
