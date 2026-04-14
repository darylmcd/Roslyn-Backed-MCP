namespace RoslynMcp.Tests;

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
    public static void ClassCleanup()
    {
        if (WorkspaceId is not null)
        {
            try { WorkspaceManager.Close(WorkspaceId); } catch { }
        }
        DeleteDirectoryIfExists(CopiedRoot);
        DisposeServices();
    }

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
    public async Task AnalyzeDataFlow_InvertedRange_ThrowsArgumentException()
    {
        // analyze-data-flow-inverted-range: pre-fix, inverted ranges fell through to the
        // misleading "No statements found in the line range 200-100" InvalidOperation. Post-fix,
        // the service rejects the input upfront with a structured ArgumentException.
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 200, endLine: 100, CancellationToken.None));
        StringAssert.Contains(ex.Message, "<=");
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_InvertedRange_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeControlFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 200, endLine: 100, CancellationToken.None));
        StringAssert.Contains(ex.Message, "<=");
    }

    [TestMethod]
    public async Task AnalyzeDataFlow_NegativeLine_ThrowsArgumentException()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            FlowAnalysisService.AnalyzeDataFlowAsync(
                WorkspaceId, TargetFilePath, startLine: 0, endLine: 5, CancellationToken.None));
    }
}
