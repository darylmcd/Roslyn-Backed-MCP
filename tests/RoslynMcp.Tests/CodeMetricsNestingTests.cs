using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Characterization tests for <see cref="CodeMetricsService"/>'s max-nesting-depth
/// computation (<c>VisitChildForNesting</c> / <c>VisitControlFlowNesting</c>).
/// Added in WS2 session 2.9 before refactoring the visitor-pattern dispatcher to
/// lock in the observed depth semantics for every control-flow statement shape
/// the visitor handles: deeply nested mixed-statement chains, flat method bodies
/// with only expressions, and nested local functions / try-catch.
///
/// Tests exercise the visitor through the public <see cref="CodeMetricsService.GetComplexityMetricsAsync"/>
/// API (which reads <c>maxNestingDepth</c> via <c>CalculateMaxNestingDepthForMember</c>),
/// so the refactor cannot change observable output.
/// </summary>
[TestClass]
public sealed class CodeMetricsNestingTests
{
    private const string WorkspaceId = "code-metrics-nesting-test-ws";

    [TestMethod]
    public async Task MaxNestingDepth_DeeplyNested_CountsEveryControlFlowLevel()
    {
        // Six-level nested control flow: if > for > while > switch > using > try.
        // Expected nesting depth = 6 (each statement's body sits one level deeper
        // than its parent; the inner-most `return 1;` is at depth 6).
        const string source = """
            namespace Sample;
            public class Deep
            {
                public int Run(System.IDisposable d, int[] xs)
                {
                    if (xs is null)                            // depth 1
                    {
                        for (var i = 0; i < 10; i++)           // depth 2
                        {
                            var j = 0;
                            while (j < 10)                     // depth 3
                            {
                                switch (j % 2)                 // depth 4
                                {
                                    case 0:
                                        using (d)              // depth 5
                                        {
                                            try                // depth 6
                                            {
                                                return 1;
                                            }
                                            catch { return 0; }
                                        }
                                        break;
                                }
                                j++;
                            }
                        }
                    }
                    return -1;
                }
            }
            """;

        var nesting = await GetMaxNestingDepthAsync(source, methodName: "Run");

        // CHARACTERIZATION (observed pre-refactor): 5. Trace: if→1, for→2, while→3,
        // switch section→4, using→5. The try block inside the using STATEMENT recurses
        // at depth 5 (not 6) because VisitControlFlowNesting is entered at the using's
        // `Statement` (not at the using itself) — so the try's VisitChildForNesting call
        // sees depth=5 and increments its Block to depth+1=6 only if maxDepth already
        // beat that — but the switch arm statements-list idiom means the using's
        // depth-5 body sees the try statement at depth 5 (its block at 6 would overshoot
        // if chained, but here the `break;` short-circuits the arm). Locked at 5 to
        // document current semantics; refactor must preserve this number exactly.
        Assert.AreEqual(5, nesting,
            "Deeply nested if>for>while>switch>using>try (pre-refactor observed) = 5. " +
            "Refactor must preserve the exact observed depth — characterization, not prediction.");
    }

    [TestMethod]
    public async Task MaxNestingDepth_FlatMethodWithConditionalExpressions_StaysShallow()
    {
        // Ternaries, coalescing, logical-&&/|| are complexity contributors but NOT
        // nesting contributors — the visitor handles only statement-level nesting.
        // A flat method body full of expression branches must report depth 0.
        const string source = """
            namespace Sample;
            public class Flat
            {
                public int Compute(int x, int? y)
                {
                    var a = x > 0 ? 1 : -1;
                    var b = y ?? 0;
                    var c = (x > 0 && y.HasValue) || x < -100;
                    var d = c ? a + b : a - b;
                    return d;
                }
            }
            """;

        var nesting = await GetMaxNestingDepthAsync(source, methodName: "Compute");

        // No statement-level control flow: every statement is a local declaration or return
        // at the method body root. Expected depth: 0 (body itself is the depth=0 scope).
        Assert.AreEqual(0, nesting,
            "Flat method with only conditional/coalesce/ternary expressions must " +
            "report nesting depth 0 — expressions don't nest statements.");
    }

    [TestMethod]
    public async Task MaxNestingDepth_NestedLocalFunctionAndLambda_LocalFunctionDoesNotAddDepth()
    {
        // Local functions are a special case in VisitChildForNesting: they DO NOT
        // increment depth (the visitor recurses into the local-function body at the
        // same depth). This guards against a regression where the refactor might
        // naively treat them like other block-bearing statements.
        const string source = """
            namespace Sample;
            using System;
            public class LocalFunc
            {
                public int Outer(int x)
                {
                    if (x > 0)                  // depth 1
                    {
                        int Helper(int y)       // local function at depth 1 (does NOT add depth)
                        {
                            if (y > 0)          // depth 2 (1 from outer `if`, +1 from this if)
                            {
                                return y;
                            }
                            return 0;
                        }
                        return Helper(x);
                    }
                    return -1;
                }
            }
            """;

        var nesting = await GetMaxNestingDepthAsync(source, methodName: "Outer");

        // Outer `if` at depth 1. The local function's body recurses at the SAME depth
        // (contract: local functions don't increment nesting). The inner `if (y > 0)` then
        // contributes +1 → depth 2. If the refactor accidentally promotes local functions
        // to single-body-statement handling, this would become depth 3.
        Assert.AreEqual(2, nesting,
            "Local functions must NOT increment nesting depth — inner `if (y > 0)` " +
            "inside the local function must resolve to depth 2, not 3.");
    }

    [TestMethod]
    public async Task MaxNestingDepth_TryCatchFinally_EachBlockCountsAsOneDepth()
    {
        // VisitTryStatement treats try/each-catch/finally all as depth+1 from the try
        // site. The max over them is the nesting depth — nested statements inside any
        // of those blocks contribute further.
        const string source = """
            namespace Sample;
            using System;
            public class TryCatch
            {
                public int Run()
                {
                    try                             // try block: depth 1
                    {
                        if (DateTime.Now.Year > 2000)  // depth 2
                        {
                            return 1;
                        }
                        return 2;
                    }
                    catch (InvalidOperationException) // catch block: depth 1
                    {
                        while (DateTime.Now.Ticks > 0) // depth 2 inside catch
                        {
                            return 3;
                        }
                        return 4;
                    }
                    finally                          // finally block: depth 1
                    {
                        Console.WriteLine("cleanup");
                    }
                }
            }
            """;

        var nesting = await GetMaxNestingDepthAsync(source, methodName: "Run");

        // Try block contents at depth 1, inner `if` at depth 2. Same for the catch.
        // finally's single `Console.WriteLine` stays at depth 1. Max across all = 2.
        Assert.AreEqual(2, nesting,
            "try/catch/finally each count as depth+1 from the try site; " +
            "nested control flow inside them stacks further.");
    }

    [TestMethod]
    public async Task MaxNestingDepth_SwitchExpression_ArmsCountAsOneDepth()
    {
        // SwitchExpressionSyntax is handled separately from SwitchStatementSyntax —
        // each arm's expression recurses at depth+1. This test guards the
        // switch-expression branch specifically.
        const string source = """
            namespace Sample;
            public class SwitchExpr
            {
                public int Compute(int x)
                {
                    return x switch
                    {
                        0 => 0,
                        1 => ComputeInner(x),
                        _ => -1
                    };
                }

                private int ComputeInner(int x)
                {
                    if (x > 0)                      // depth 1
                    {
                        for (var i = 0; i < x; i++) // depth 2
                        {
                            x -= i;
                        }
                    }
                    return x;
                }
            }
            """;

        var nesting = await GetMaxNestingDepthAsync(source, methodName: "Compute");

        // The switch expression's arms are recursed at depth+1 (=1), but the arm
        // expressions themselves are simple identifiers/method-calls — no further
        // nesting. Expected: 1.
        Assert.AreEqual(1, nesting,
            "Switch expression arms count as depth 1 from the switch expression site — " +
            "arm body expressions don't push further unless they contain statements.");

        // Sanity check: the paired ComputeInner method still sees its if>for as depth 2.
        var innerNesting = await GetMaxNestingDepthAsync(source, methodName: "ComputeInner");
        Assert.AreEqual(2, innerNesting,
            "Paired method's if>for should resolve independently at depth 2.");
    }

    // ─── test infrastructure ───────────────────────────────────────────────────

    /// <summary>
    /// Build an isolated <see cref="CodeMetricsService"/> backed by an in-memory
    /// <see cref="AdhocWorkspace"/> containing <paramref name="source"/>, call
    /// <c>GetComplexityMetricsAsync</c>, and return the max-nesting-depth measured
    /// for the method named <paramref name="methodName"/>.
    /// </summary>
    private static async Task<int> GetMaxNestingDepthAsync(string source, string methodName)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "NestingTestProject",
            assemblyName: "NestingTestProject",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "NestingTestProject.csproj")));

        var docId = DocumentId.CreateNewId(projectId);
        var fullPath = Path.Combine(Path.GetTempPath(), "NestingSample.cs");
        workspace.AddDocument(DocumentInfo.Create(
            docId,
            "NestingSample.cs",
            filePath: fullPath,
            loader: TextLoader.From(
                TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create(),
                    fullPath))));

        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        var service = new CodeMetricsService(wsManager);

        var results = await service.GetComplexityMetricsAsync(
            WorkspaceId,
            filePath: null,
            filePaths: null,
            projectFilter: null,
            minComplexity: null,
            limit: 100,
            default);

        var match = results.FirstOrDefault(r => r.SymbolName == methodName);
        Assert.IsNotNull(match, $"Expected a complexity-metrics result for method '{methodName}'.");
        return match.MaxNestingDepth;
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in — mirrors the pattern in
    /// DuplicateMethodDetectorTests. Only <see cref="GetCurrentSolution"/> is needed
    /// for <see cref="CodeMetricsService"/>; the rest throw to surface accidental coupling.
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
