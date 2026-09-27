using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// donotparallelize-audit-wave-12: [DoNotParallelize] removed. ClassInit loads a private SampleSolution copy
// (CreateSampleSolutionCopy, under the per-process TestTempRoot) into its own workspace id; the one on-disk
// mutation (AnalyzeControlFlow_FullMethodRange_...) and its ReloadAsync target only that private copy and
// workspace, and ClassCleanup closes it. This is the same isolated-copy load/reload/close shape that the
// parallel IsolatedWorkspaceTestBase classes (e.g. ExternalEditStalenessTests, EditUndoIntegrationTests)
// already run concurrently; tests within this class still run sequentially under class-level scope, so the
// fixed-line-offset restore in that test is unaffected. Verified with a repeated (3x) concurrent run
// alongside its wave-12 siblings and other parallel isolated-workspace classes, green every time.
[TestClass]
public sealed class FlowAnalysisServiceTests : SharedWorkspaceTestBase
{
    private static string CopiedRoot { get; set; } = null!;
    private static string CopiedSolutionPath { get; set; } = null!;
    private static string TargetFilePath { get; set; } = null!;
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        CopiedSolutionPath = CreateSampleSolutionCopy();
        CopiedRoot = Path.GetDirectoryName(CopiedSolutionPath)!;
        TargetFilePath = Path.Combine(CopiedRoot, "SampleLib", "ExpressionBodiedSamples.cs");

        // The fixture deliberately exercises both an expression-bodied method (RulesEqual)
        // and an expression-bodied property (Count) so the lift code path is covered for
        // both syntactic shapes.
        await File.WriteAllTextAsync(TargetFilePath, """
namespace SampleLib;

public class ExpressionBodiedSamples
{
    private readonly System.Collections.Generic.List<int> _items = new();

    public bool RulesEqual(int a, int b, int c, int d) => a == b && c == d;

    public int Count => _items.Count;

    public int CountWithStatementBody()
    {
        return _items.Count;
    }
}
""", CancellationToken.None);

        var status = await WorkspaceManager.LoadAsync(CopiedSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static async Task ClassCleanup() =>
        await CleanupFailureCollector.RunAsync(
            "Failed to dispose the flow-analysis fixture.",
            CleanupFailureCollector.FromAction(() =>
            {
                if (WorkspaceId is not null)
                {
                    WorkspaceManager.Close(WorkspaceId);
                }
            }),
            CleanupFailureCollector.FromAction(() => DeleteDirectoryIfExists(CopiedRoot)),
            CleanupFailureCollector.FromAction(DisposeServices));

    [TestMethod]
    public async Task AnalyzeDataFlow_ExpressionBodiedMethod_LiftsAndReturnsParameters()
    {
        // RulesEqual is on line 7 of the fixture (1-based).
        var result = await FlowAnalysisService.AnalyzeDataFlowAsync(
            WorkspaceId, TargetFilePath, startLine: 7, endLine: 7, CancellationToken.None);

        Assert.IsTrue(result.Succeeded,
            $"Expected data flow analysis to succeed for expression-bodied member. " +
            $"ReadInside={string.Join(",", result.ReadInside)}");
        // SymbolNames uses SymbolDisplayFormat.MinimallyQualifiedFormat, which renders
        // parameters as "int a", "int b", etc. Check that all four parameters appear.
        var names = result.ReadInside.ToList();
        Assert.IsTrue(names.Any(n => n.EndsWith(" a", StringComparison.Ordinal)),
            $"ReadInside should contain parameter 'a'. Actual: [{string.Join(", ", names)}]");
        Assert.IsTrue(names.Any(n => n.EndsWith(" b", StringComparison.Ordinal)));
        Assert.IsTrue(names.Any(n => n.EndsWith(" c", StringComparison.Ordinal)));
        Assert.IsTrue(names.Any(n => n.EndsWith(" d", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_ExpressionBodiedMethod_ReturnsSyntheticImplicitReturn()
    {
        var result = await FlowAnalysisService.AnalyzeControlFlowAsync(
            WorkspaceId, TargetFilePath, startLine: 7, endLine: 7, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.StartPointIsReachable);
        Assert.IsFalse(result.EndPointIsReachable, "Expression body's implicit return makes the end point unreachable.");
        Assert.AreEqual(1, result.ReturnStatements.Count, "Expression body should yield exactly one synthetic return.");
        Assert.AreEqual(0, result.EntryPoints.Count);
        Assert.AreEqual(0, result.ExitPoints.Count);
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_ExpressionBodiedProperty_ReturnsSyntheticImplicitReturn()
    {
        // Count is on line 9 of the fixture.
        var result = await FlowAnalysisService.AnalyzeControlFlowAsync(
            WorkspaceId, TargetFilePath, startLine: 9, endLine: 9, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.ReturnStatements.Count);
        Assert.IsNotNull(result.ReturnStatements[0].ExpressionText);
        StringAssert.Contains(result.ReturnStatements[0].ExpressionText!, "_items");
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_StatementBodiedMethod_StillUsesStatementPath()
    {
        // CountWithStatementBody body lives on line 13 (the `return _items.Count;` line).
        var result = await FlowAnalysisService.AnalyzeControlFlowAsync(
            WorkspaceId, TargetFilePath, startLine: 13, endLine: 13, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.ReturnStatements.Count);
        // Statement-bodied path's synthesized warning is null when EndPointIsReachable is consistent.
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_FullMethodRange_NoSpuriousPartialSliceWarning()
    {
        // analyze-control-flow-partial-slice-warning-on-full-method (gh #743): pre-fix, a
        // complete void method body whose range was supplied in full produced a succeeded
        // analysis with zero entry/exit/return counts, and the unified warning branch
        // misreported the result as "incomplete for this line range. Prefer a range that
        // covers full statement blocks within a single method body (not a partial slice of
        // a method)." Post-fix, that branch fires only when Succeeded == false; a succeeded
        // zero-count result for a void block now returns Warning == null.

        // Inject a void method fixture into the same target file. Append to the existing
        // class body just before the closing brace so the existing tests at fixed line
        // numbers stay valid.
        var originalContent = await File.ReadAllTextAsync(TargetFilePath, CancellationToken.None);
        var augmented = originalContent.TrimEnd().TrimEnd('}', '\r', '\n')
            + Environment.NewLine
            + Environment.NewLine
            + "    public void VoidMethod() { int x = 1; }" + Environment.NewLine
            + "}" + Environment.NewLine;

        try
        {
            await File.WriteAllTextAsync(TargetFilePath, augmented, CancellationToken.None);
            await WorkspaceManager.ReloadAsync(WorkspaceId, CancellationToken.None);

            // VoidMethod lands on line 16 of the augmented fixture (original 15 lines + blank line + method line).
            // Locate it dynamically so a small fixture shift cannot break the test.
            var lines = augmented.Split('\n');
            int methodLine = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("VoidMethod()"))
                {
                    methodLine = i + 1; // 1-based
                    break;
                }
            }
            Assert.IsTrue(methodLine > 0, "Could not locate VoidMethod in augmented fixture.");

            var result = await FlowAnalysisService.AnalyzeControlFlowAsync(
                WorkspaceId, TargetFilePath, startLine: methodLine, endLine: methodLine, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, "Roslyn should succeed on a complete void method body.");
            Assert.AreEqual(0, result.EntryPoints.Count);
            Assert.AreEqual(0, result.ExitPoints.Count);
            Assert.AreEqual(0, result.ReturnStatements.Count, "Void body has no explicit returns.");
            Assert.IsNull(result.Warning,
                $"Expected no warning on a succeeded zero-count void method analysis. Actual: {result.Warning}");
        }
        finally
        {
            // Restore the fixture so other tests in this class continue to see fixed line offsets.
            await File.WriteAllTextAsync(TargetFilePath, originalContent, CancellationToken.None);
            await WorkspaceManager.ReloadAsync(WorkspaceId, CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task AnalyzeDataFlow_InvertedRange_ThrowsArgumentException()
    {
        // analyze-data-flow-inverted-range: pre-fix, inverted ranges fell through to the
        // misleading "No statements found in the line range 200-100" InvalidOperation. Post-fix,
        // the service rejects the input upfront with a structured ArgumentException.
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 200, endLine: 100, CancellationToken.None));
        StringAssert.Contains(ex.Message, "<=");
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_InvertedRange_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeControlFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 200, endLine: 100, CancellationToken.None));
        StringAssert.Contains(ex.Message, "<=");
    }

    [TestMethod]
    public async Task AnalyzeDataFlow_NegativeLine_ThrowsArgumentException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 0, endLine: 5, CancellationToken.None));
    }

    private const string TwoMethodFixture = """
    public int FirstMethod(int a)
    {
        var x = a + 1; // MARK-FIRST-START
        return x;
    }

    public int SecondMethod(int b)
    {
        var y = b * 2; // MARK-SECOND-START
        var z = y + 1;
        return z; // MARK-SECOND-END
    }
""";

    [TestMethod]
    public async Task FlowAnalysis_RangeSpanningTwoMethods_ReportsNarrowedEffectiveRange()
    {
        // flow-analysis-silent-region-narrowing: pre-fix, a range starting in FirstMethod and
        // ending in SecondMethod was silently narrowed to SecondMethod's statements (the largest
        // single-block group) with no trace in either DTO.
        await WithAugmentedFixtureAsync(TwoMethodFixture, async lines =>
        {
            var requestedStart = LineOf(lines, "MARK-FIRST-START");
            var effectiveStart = LineOf(lines, "MARK-SECOND-START");
            var requestedEnd = LineOf(lines, "MARK-SECOND-END");

            var dataFlow = await FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, requestedStart, requestedEnd, CancellationToken.None);

            Assert.IsTrue(dataFlow.Succeeded);
            Assert.AreEqual(effectiveStart, dataFlow.EffectiveStartLine);
            Assert.AreEqual(requestedEnd, dataFlow.EffectiveEndLine);
            Assert.IsNotNull(dataFlow.Warning, "A narrowed data-flow region must carry a warning.");
            StringAssert.Contains(dataFlow.Warning, $"{requestedStart}-{requestedEnd}");
            StringAssert.Contains(dataFlow.Warning, $"{effectiveStart}-{requestedEnd}");
            StringAssert.Contains(dataFlow.Warning, "2 statement(s)");
            StringAssert.Contains(dataFlow.Warning, "more than one member");
            Assert.IsFalse(dataFlow.Warning.Contains(TargetFilePath, StringComparison.OrdinalIgnoreCase),
                "The warning must not echo the file path.");

            var controlFlow = await FlowAnalysisService.AnalyzeControlFlowAsync(
                WorkspaceId, TargetFilePath, requestedStart, requestedEnd, CancellationToken.None);

            Assert.IsTrue(controlFlow.Succeeded);
            Assert.AreEqual(effectiveStart, controlFlow.EffectiveStartLine);
            Assert.AreEqual(requestedEnd, controlFlow.EffectiveEndLine);
            Assert.IsNotNull(controlFlow.Warning);
            StringAssert.StartsWith(controlFlow.Warning, dataFlow.Warning,
                "The narrowing notice must lead the control-flow warning.");
            // SecondMethod ends in `return z;`, so the Roslyn fall-through warning must survive
            // alongside the narrowing notice rather than being overwritten.
            StringAssert.Contains(controlFlow.Warning, "EndPointIsReachable is false");
            Assert.AreEqual(1, controlFlow.ReturnStatements.Count,
                "Only SecondMethod's return is inside the effective region.");
        });
    }

    [TestMethod]
    public async Task FlowAnalysis_RangeWithinOneMethod_LeavesNarrowingFieldsNull()
    {
        await WithAugmentedFixtureAsync(TwoMethodFixture, async lines =>
        {
            var start = LineOf(lines, "MARK-SECOND-START");
            var end = LineOf(lines, "MARK-SECOND-END");

            var dataFlow = await FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, start, end, CancellationToken.None);

            Assert.IsTrue(dataFlow.Succeeded);
            Assert.IsNull(dataFlow.Warning);
            Assert.IsNull(dataFlow.EffectiveStartLine);
            Assert.IsNull(dataFlow.EffectiveEndLine);

            // The new optional fields must be absent from the wire shape when unset.
            var json = System.Text.Json.JsonSerializer.Serialize(dataFlow, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
            Assert.IsFalse(json.Contains("\"warning\"", StringComparison.Ordinal), json);
            Assert.IsFalse(json.Contains("effectiveStartLine", StringComparison.Ordinal), json);
            Assert.IsFalse(json.Contains("effectiveEndLine", StringComparison.Ordinal), json);

            var controlFlow = await FlowAnalysisService.AnalyzeControlFlowAsync(
                WorkspaceId, TargetFilePath, start, end, CancellationToken.None);

            Assert.IsTrue(controlFlow.Succeeded);
            Assert.IsNull(controlFlow.EffectiveStartLine);
            Assert.IsNull(controlFlow.EffectiveEndLine);
            Assert.IsFalse(controlFlow.Warning?.Contains("narrowed", StringComparison.Ordinal) ?? false,
                $"A single-method range must not report narrowing. Actual: {controlFlow.Warning}");
        });
    }

    /// <summary>
    /// Appends <paramref name="members"/> to the fixture class, reloads, runs
    /// <paramref name="body"/> with the augmented file's lines, then restores the fixture so
    /// the fixed-line tests above keep their offsets.
    /// </summary>
    private static async Task WithAugmentedFixtureAsync(string members, Func<string[], Task> body)
    {
        var originalContent = await File.ReadAllTextAsync(TargetFilePath, CancellationToken.None);
        var augmented = originalContent.TrimEnd().TrimEnd('}', '\r', '\n')
            + Environment.NewLine
            + Environment.NewLine
            + members.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine
            + "}" + Environment.NewLine;

        try
        {
            await File.WriteAllTextAsync(TargetFilePath, augmented, CancellationToken.None);
            await WorkspaceManager.ReloadAsync(WorkspaceId, CancellationToken.None);
            await body(augmented.Split('\n'));
        }
        finally
        {
            await File.WriteAllTextAsync(TargetFilePath, originalContent, CancellationToken.None);
            await WorkspaceManager.ReloadAsync(WorkspaceId, CancellationToken.None);
        }
    }

    private static int LineOf(string[] lines, string marker)
    {
        var index = Array.FindIndex(lines, l => l.Contains(marker, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"Could not locate marker '{marker}' in the augmented fixture.");
        return index + 1;
    }
}
