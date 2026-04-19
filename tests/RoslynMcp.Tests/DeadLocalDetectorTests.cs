using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="UnusedCodeAnalyzer.FindDeadLocalsAsync"/>. Uses an
/// in-memory <see cref="AdhocWorkspace"/> seeded with the BCL so semantic-model
/// data-flow analysis resolves <c>Console.WriteLine</c>, <c>TryParse</c>, etc. The
/// detector's contract: flag method-local <c>ILocalSymbol</c>s that appear in the
/// body's <c>WrittenInside</c> set but NOT in its <c>ReadInside</c> set, modulo the
/// language-shape exclusions enumerated in the interface doc.
/// </summary>
[TestClass]
public sealed class DeadLocalDetectorTests
{
    private const string WorkspaceId = "dead-local-test-ws";

    // The positive case from the initiative's Validation field.
    [TestMethod]
    public async Task FindDeadLocals_VarAssignedNeverRead_IsFlagged()
    {
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static void Process()
                {
                    var x = 5;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count, "Expected the write-never-read local to be flagged.");
        Assert.AreEqual("x", hits[0].SymbolName);
        Assert.AreEqual("Process", hits[0].ContainingMethod);
        Assert.AreEqual("Service", hits[0].ContainingType);
    }

    // The negative case from the initiative's Validation field.
    [TestMethod]
    public async Task FindDeadLocals_VarAssignedThenRead_IsNotFlagged()
    {
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static void Process()
                {
                    var x = 5;
                    System.Console.WriteLine(x);
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Local that is read after its write must not be flagged. Got: "
            + string.Join(", ", hits.Select(h => h.SymbolName)));
    }

    // The third validation case: `out var x` at a call site — the callee writes
    // into x; we intentionally don't flag even if x is never read after the call
    // (IDE0059 separately suggests the `out _` rewrite and flagging produces noise).
    [TestMethod]
    public async Task FindDeadLocals_OutVarAtCallSite_IsNotFlagged()
    {
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static bool TryDo(out int value)
                {
                    value = 7;
                    return true;
                }
                public static void Caller()
                {
                    TryDo(out var x);
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        // Only x in Caller could match. The `value` parameter inside TryDo is an
        // IParameterSymbol, not an ILocalSymbol, so the analyzer already excludes it.
        Assert.AreEqual(0, hits.Count,
            "out-var declarations at call sites are intentionally not flagged even when unused. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName)));
    }

    [TestMethod]
    public async Task FindDeadLocals_ForeachIterationVariable_IsNotFlagged()
    {
        // The foreach iteration variable's write is the loop step, not user intent.
        // Even if the body doesn't read `item`, flagging is noise — the idiomatic
        // fix is `for (var i = 0; i < xs.Length; i++)`, not name removal.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static int Count(int[] xs)
                {
                    var n = 0;
                    foreach (var item in xs)
                    {
                        n++;
                    }
                    return n;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "foreach iteration variables must not be flagged. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_UsingResourceLocal_IsNotFlagged()
    {
        // `using var stream = ...` — the local exists to scope a Dispose call. Its
        // "read" is the implicit Dispose at end-of-scope; dead-local detection must
        // not interpret absence of an explicit read as waste.
        const string source = """
            namespace Sample;
            using System.IO;
            internal static class Service
            {
                public static void Write(string path)
                {
                    using var stream = new MemoryStream();
                    stream.WriteByte(1);
                }
                public static void OnlyDispose(string path)
                {
                    using var stream = new MemoryStream();
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "using-var locals must not be flagged as dead. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_PatternDesignation_IsNotFlagged()
    {
        // `is T x` — the designated local is required by the pattern shape; the
        // value isn't necessarily read (callers frequently just want the type test).
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static bool IsInt(object o)
                {
                    if (o is int x)
                    {
                        return true;
                    }
                    return false;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Pattern-designation locals must not be flagged. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_TupleDeconstructionNamedHalfDead_IsNotFlagged()
    {
        // Deconstruction designation: `var (_, b) = Foo()`. Even if `b` is never
        // read, positional deconstruction requires the name slot. Flagging is noise.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static (int, int) Pair() => (1, 2);
                public static int Caller()
                {
                    var (_, b) = Pair();
                    return 0;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Tuple-deconstruction designations must not be flagged. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_CatchExceptionLocal_IsNotFlagged()
    {
        // `catch (Exception ex)` — the local is syntactically required by the
        // catch clause even when the handler doesn't inspect it. IDE0060 covers
        // the `catch (Exception _)` rewrite separately.
        const string source = """
            namespace Sample;
            using System;
            internal static class Service
            {
                public static void SafeCall(Action a)
                {
                    try { a(); }
                    catch (Exception ex)
                    {
                    }
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "catch-clause exception locals must not be flagged. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_RefParameter_IsNotFlagged()
    {
        // Ref/in parameters appear in WrittenInside when assigned but are IParameterSymbol,
        // not ILocalSymbol. Guard belt-and-suspenders by asserting the analyzer never
        // emits a hit against a ref parameter.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static void Bump(ref int x)
                {
                    x = x + 1;
                }
                public static void Fill(in int y)
                {
                    // no-op
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Ref/in parameters must not be flagged (they are parameters, not locals). "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_ConstLocal_IsNotFlagged()
    {
        // Named compile-time constants. `nameof(LocalConst)` could reach them; removing
        // the name changes API shape. Skip.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static string Tag()
                {
                    const string LocalConst = "hi";
                    return "done";
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "const locals must not be flagged. "
            + "Got: " + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_LocalReassignedThenRead_IsNotFlagged()
    {
        // Write then overwrite then read — the latest written value is the one that's
        // read, and the compiler-style "last write before first read" check (which the
        // DataFlow API does NOT do) isn't the same as dead-local detection. For this
        // tool's contract (`WrittenInside \ ReadInside`), the local IS in ReadInside,
        // so it's not flagged. This test pins that behavior.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static int Compute(int seed)
                {
                    var n = seed;
                    n = n * 2;
                    return n;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "A local that is read at any point is not flagged under the WrittenInside \\ ReadInside contract.");
    }

    [TestMethod]
    public async Task FindDeadLocals_MultipleBodiesWithDead_ReportsEach()
    {
        // Two independent bodies each with their own dead local. Confirms the walker
        // descends into each method-like body and reports per-body hits.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static void A()
                {
                    var aa = 1;
                }
                public static void B()
                {
                    var bb = 2;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        Assert.AreEqual(2, hits.Count, "Expected one hit per method with a dead local.");
        var names = hits.Select(h => h.SymbolName).OrderBy(s => s).ToArray();
        CollectionAssert.AreEqual(new[] { "aa", "bb" }, names);
    }

    [TestMethod]
    public async Task FindDeadLocals_LocalFunctionBody_IsWalked()
    {
        // Local functions are inside a parent method body but have their own
        // data-flow scope. The walker must descend into them so dead locals in a
        // nested local function are flagged too.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static int Entry()
                {
                    return Inner();

                    int Inner()
                    {
                        var temp = 42;
                        return 0;
                    }
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions(),
            default);

        // The `temp` local in Inner is dead. Entry itself has no local writes.
        Assert.IsTrue(
            hits.Any(h => h.SymbolName == "temp" && h.ContainingMethod == "Inner"),
            "Dead local inside a local function must be flagged. Got: "
            + string.Join(", ", hits.Select(h => h.SymbolName + " in " + h.ContainingMethod)));
    }

    [TestMethod]
    public async Task FindDeadLocals_LimitCapsResults()
    {
        // Verify the Limit option clamps results.
        const string source = """
            namespace Sample;
            internal static class Service
            {
                public static void A() { var a1 = 1; }
                public static void B() { var b1 = 2; }
                public static void C() { var c1 = 3; }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadLocalsAsync(
            WorkspaceId,
            new DeadLocalsAnalysisOptions { Limit = 2 },
            default);

        Assert.AreEqual(2, hits.Count, "Result count must be capped by Limit.");
    }

    /// <summary>
    /// Builds an <see cref="UnusedCodeAnalyzer"/> against a single-file AdhocWorkspace
    /// with the BCL reference loaded. Mirrors the helper in
    /// <see cref="DuplicateHelperDetectionTests"/>; the two test suites exercise
    /// different public methods on the same analyzer and need the same harness.
    /// </summary>
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
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.IO.MemoryStream).Assembly.Location),
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

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in — only the surface the analyzer
    /// and the compilation cache touch is wired; the rest throws to surface accidental
    /// coupling. Duplicated rather than shared to keep the test file self-contained.
    /// </summary>
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
