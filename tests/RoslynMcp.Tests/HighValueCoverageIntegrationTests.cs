using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Targets P1/P2 coverage gaps: CompileCheckService, CodeActionService,
/// DependencyAnalysisService, DeadCodeService (see docs/coverage-baseline.md).
/// </summary>
[TestClass]
public sealed class HighValueCoverageIntegrationTests : SharedWorkspaceTestBase
{
    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    [TestMethod]
    public async Task CompileCheck_BuildFailureSolution_Reports_Errors()
    {
        var status = await WorkspaceManager.LoadAsync(BuildFailureSolutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;

        var result = await CompileCheckService.CheckAsync(
            workspaceId, projectFilter: null, emitValidation: false, CancellationToken.None);

        Assert.IsFalse(result.Success, "Broken solution should report compile errors.");
        Assert.IsTrue(result.ErrorCount > 0, "Expected at least one error for MissingSymbol.");
        Assert.IsNotNull(result.Diagnostics);
        Assert.IsTrue(
            result.Diagnostics.Any(d => d.Id.Contains("CS", StringComparison.Ordinal)),
            "Expected compiler diagnostics.");
    }

    [TestMethod]
    public async Task CompileCheck_SampleSolution_Cancellation_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            CompileCheckService.CheckAsync(SampleWorkspaceId, null, false, cts.Token));
    }

    [TestMethod]
    public async Task CodeAction_GetCodeActions_Unknown_File_Returns_Empty()
    {
        var actions = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.cs"),
            startLine: 1,
            startColumn: 1,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        Assert.AreEqual(0, actions.Count);
    }

    [TestMethod]
    public async Task CodeAction_PreviewCodeAction_Invalid_Index_Throws()
    {
        var filePath = FindDocumentPath("AnimalService.cs");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            CodeActionService.PreviewCodeActionAsync(
                SampleWorkspaceId,
                filePath,
                startLine: 16,
                startColumn: 17,
                endLine: null,
                endColumn: null,
                actionIndex: 9_999,
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "index");
    }

    [TestMethod]
    public async Task DependencyAnalysis_GetNamespaceDependencies_Full_Solution_Has_Nodes_And_Edges()
    {
        var graph = await DependencyAnalysisService.GetNamespaceDependenciesAsync(
            SampleWorkspaceId, projectFilter: null, CancellationToken.None);

        Assert.IsTrue(graph.Nodes.Count > 0);
        Assert.IsNotNull(graph.Edges);
    }

    [TestMethod]
    public async Task DependencyAnalysis_GetNuGetDependencies_Returns_Packages_For_SampleSolution()
    {
        var result = await DependencyAnalysisService.GetNuGetDependenciesAsync(
            SampleWorkspaceId, CancellationToken.None);

        Assert.IsTrue(result.Projects.Count > 0);
        Assert.IsTrue(
            result.Packages.Any(p => p.PackageId.Length > 0),
            "Expected at least one NuGet package reference across projects.");
    }

    [TestMethod]
    public async Task DeadCode_PreviewRemoveDeadCode_Empty_Throws()
    {
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            DeadCodeService.PreviewRemoveDeadCodeAsync(
                SampleWorkspaceId,
                new DeadCodeRemovalDto([]),
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "symbol");
    }
}
