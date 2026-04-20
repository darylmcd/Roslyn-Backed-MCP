using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Targeted tests for the FirewallAnalyzer rw-lock audit (2026-04-07) fixes:
/// FLAG-A (UnresolvedAnalyzerReference cascade), FLAG-B (compile_check pagination),
/// FLAG-C (analyze_snippet column offset), FLAG-006 (return-type token resolution),
/// FLAG-007 (analyze_snippet returnExpression kind), tool: "unknown" propagation,
/// and the P3 per-response _meta diagnostics summary.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AuditFixesTests : SharedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await LoadSharedSampleWorkspaceAsync();
    }

    // ── FLAG-C: analyze_snippet column offset for kind=statements ──

    [TestMethod]
    public async Task AnalyzeSnippet_Statements_BrokenCode_ReportsUserColumn_NotWrappedColumn()
    {
        // The audit's exact reproducer: `int x = "hello";` reports CS0029 at the literal.
        // Pre-fix: column 66 (relative to the wrapped emit). Post-fix: column ≈ 9 (where
        // "hello" actually starts in the user input — `int x = ` is 8 chars, then the literal).
        var result = await SnippetAnalysisService.AnalyzeAsync(
            "int x = \"hello\";",
            usings: null,
            kind: "statements",
            CancellationToken.None);

        Assert.IsFalse(result.IsValid, "Expected a CS0029 error for the broken assignment.");
        var error = result.Diagnostics.FirstOrDefault(d => d.Id == "CS0029");
        Assert.IsNotNull(error, "Expected CS0029 in diagnostics.");
        Assert.AreEqual(1, error.StartLine, "User code is single-line; line should be 1.");
        Assert.IsTrue(
            error.StartColumn is >= 7 and <= 11,
            $"Expected user-relative column ~9 for the literal; got {error.StartColumn}.");
    }

    // ── FLAG-007: analyze_snippet returnExpression kind ──

    [TestMethod]
    public async Task AnalyzeSnippet_ReturnExpression_AcceptsReturnStatement()
    {
        // The 'statements' wrapper used `void Run() { ... }` so `return 42;` triggered CS0127.
        // The new 'returnExpression' wrapper uses `object? Run() { ... }`.
        var result = await SnippetAnalysisService.AnalyzeAsync(
            "return 42;",
            usings: null,
            kind: "returnExpression",
            CancellationToken.None);

        Assert.IsTrue(result.IsValid,
            $"Expected returnExpression to accept `return 42;`. Errors: {string.Join(", ", result.Diagnostics.Select(d => d.Id + ": " + d.Message))}");
    }

    [TestMethod]
    public async Task AnalyzeSnippet_Statements_RejectsReturnStatement_DocumentedBehavior()
    {
        // Sanity: the 'statements' kind still wraps in void Run(), so return 42; is still
        // rejected (this is by design — the new returnExpression kind is the workaround).
        var result = await SnippetAnalysisService.AnalyzeAsync(
            "return 42;",
            usings: null,
            kind: "statements",
            CancellationToken.None);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "CS0127"),
            "Expected CS0127 for return-with-value in void method.");
    }

    // ── FLAG-B: compile_check pagination ──

    [TestMethod]
    public async Task CompileCheck_Pagination_LimitOne_ReportsHasMoreWhenMultipleDiagnostics()
    {
        // The sample solution may compile clean; we just want to verify the pagination
        // contract — TotalDiagnostics, ReturnedDiagnostics, Offset, Limit, HasMore must be
        // present and self-consistent regardless of how many diagnostics exist.
        var result = await CompileCheckService.CheckAsync(
            _workspaceId,
            new CompileCheckOptions(Limit: 1),
            CancellationToken.None);

        Assert.AreEqual(0, result.Offset);
        Assert.AreEqual(1, result.Limit);
        Assert.IsTrue(result.ReturnedDiagnostics <= 1, "ReturnedDiagnostics must respect limit.");
        Assert.AreEqual(result.ReturnedDiagnostics, result.Diagnostics.Count);
        if (result.TotalDiagnostics > 1)
            Assert.IsTrue(result.HasMore, "HasMore should be true when TotalDiagnostics > Limit.");
        else
            Assert.IsFalse(result.HasMore);
    }

    [TestMethod]
    public async Task CompileCheck_SeverityFilter_OnlyReturnsErrors()
    {
        var result = await CompileCheckService.CheckAsync(
            _workspaceId,
            new CompileCheckOptions(SeverityFilter: "Error", Limit: 100),
            CancellationToken.None);

        Assert.IsTrue(result.Diagnostics.All(d => d.Severity == "Error"),
            "Severity filter should restrict to Error diagnostics only.");
    }

    // ── AmbientGateMetrics: scope/snapshot semantics ──

    [TestMethod]
    public void AmbientGateMetrics_OutsideScope_SnapshotIsNull()
    {
        // Note: don't make assertions across other tests in this class because the test runner
        // shares AsyncLocal state. Within a single sync method we can be sure no scope is open.
        Assert.IsNull(AmbientGateMetrics.Snapshot());
    }

    [TestMethod]
    public void AmbientGateMetrics_BeginRequest_BuilderIsCurrent_AndDisposes()
    {
        Assert.IsNull(AmbientGateMetrics.Current);
        using (AmbientGateMetrics.BeginRequest())
        {
            var current = AmbientGateMetrics.Current;
            Assert.IsNotNull(current);
            current.GateMode = "rw-lock";
            current.QueuedMs = 12;
            current.HeldMs = 34;
            var snapshot = AmbientGateMetrics.Snapshot();
            Assert.IsNotNull(snapshot);
            Assert.AreEqual("rw-lock", snapshot.GateMode);
            Assert.AreEqual(12, snapshot.QueuedMs);
            Assert.AreEqual(34, snapshot.HeldMs);
        }
        Assert.IsNull(AmbientGateMetrics.Current);
    }

    [TestMethod]
    public void AmbientGateMetrics_NestedScopes_RestoreParentOnDispose()
    {
        using (AmbientGateMetrics.BeginRequest())
        {
            AmbientGateMetrics.Current!.GateMode = "outer";
            using (AmbientGateMetrics.BeginRequest())
            {
                AmbientGateMetrics.Current!.GateMode = "inner";
                Assert.AreEqual("inner", AmbientGateMetrics.Snapshot()?.GateMode);
            }
            Assert.AreEqual("outer", AmbientGateMetrics.Snapshot()?.GateMode);
        }
    }

    // ── ToolErrorHandler: tool name propagation + _meta injection ──

    [TestMethod]
    public async Task ToolErrorHandler_ErrorPayload_ContainsActualToolName_NotUnknown()
    {
        // Pre-fix: every error reported `tool: "unknown"` because no caller passed toolName.
        // Post-fix: toolName is required and propagates into the error payload.
        var result = await ToolExecutionTestHarness.RunAsync(
            "compile_check",
            () => throw new KeyNotFoundException("not found"));

        var json = JsonDocument.Parse(result);
        var toolField = json.RootElement.GetProperty("tool").GetString();
        Assert.AreEqual("compile_check", toolField);
        Assert.AreNotEqual("unknown", toolField);
    }

    [TestMethod]
    public async Task ToolErrorHandler_SuccessResponse_HasMetaField_WhenObjectRoot()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => Task.FromResult("""{"foo":"bar"}"""));

        var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.TryGetProperty("_meta", out var meta),
            "Expected _meta field on object-rooted success response.");
        Assert.IsTrue(meta.TryGetProperty("queuedMs", out _),
            $"_meta should expose queuedMs. Actual: {meta}");
    }

    [TestMethod]
    public async Task ToolErrorHandler_SuccessResponse_MetaIncludesElapsedMs()
    {
        // BUG fix (reader-tool-elapsed-ms): every tool response should expose wall-clock
        // elapsed time so concurrency audits can compute speedup ratios from inside the
        // agent loop without external instrumentation.
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            async () =>
            {
                await Task.Delay(20);
                return "{}";
            });

        var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.TryGetProperty("_meta", out var meta));
        Assert.IsTrue(meta.TryGetProperty("elapsedMs", out var elapsed),
            $"_meta should expose elapsedMs. Actual: {meta}");
        Assert.IsTrue(elapsed.GetInt64() >= 0,
            "elapsedMs should be a non-negative integer reflecting wall-clock time of the action.");
    }

    [TestMethod]
    public async Task ToolErrorHandler_ArrayRootResponse_LeftUnchanged_NoMetaWrapping()
    {
        // Backward-compat guarantee: array roots are NOT wrapped in {data, _meta}.
        // Some tools (e.g. source_generated_documents) return bare arrays.
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => Task.FromResult("[1,2,3]"));

        var json = JsonDocument.Parse(result);
        Assert.AreEqual(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [TestMethod]
    public async Task ToolErrorHandler_ErrorPayload_HasMetaField()
    {
        // _meta should also appear on error payloads so clients can observe gate state when
        // a request fails inside the action.
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new InvalidOperationException("boom"));

        var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.TryGetProperty("_meta", out _));
    }
}
