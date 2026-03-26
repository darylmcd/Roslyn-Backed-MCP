using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CohesionAnalysisTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetCohesionMetrics_ReturnsMetrics()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, null, 2, 50, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0, "Expected at least one type with cohesion metrics");

        var first = result[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.TypeName));
        Assert.IsTrue(first.Lcom4Score >= 1, "LCOM4 score should be at least 1");
    }

    [TestMethod]
    public async Task FindSharedMembers_RunsWithoutError()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalService");
        var result = await CohesionAnalysisService.FindSharedMembersAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task GetCohesionMetrics_WithFilePath_FiltersByFile()
    {
        var doc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        Assert.IsNotNull(doc?.FilePath, "AnimalService.cs not found in workspace");

        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, doc.FilePath, null, 1, 50, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.All(m =>
            m.FilePath?.Contains("AnimalService.cs") == true),
            "All results should be from the filtered file");
    }
}
