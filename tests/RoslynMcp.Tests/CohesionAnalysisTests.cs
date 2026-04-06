using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CohesionAnalysisTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetCohesionMetrics_WhenExcludeTestProjects_OmitsTestProjectSources()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, null, 2, 200, includeInterfaces: false, excludeTestProjects: true, CancellationToken.None);

        Assert.IsTrue(
            result.All(m => m.FilePath is null ||
                !m.FilePath.Contains("SampleLib.Tests", StringComparison.OrdinalIgnoreCase)),
            "Types from MSBuild test projects should be omitted when excludeTestProjects is true.");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_ReturnsMetrics()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, null, 2, 50, includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

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
            WorkspaceId, doc.FilePath, null, 1, 50, includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0, "Expected at least one metric from filtered file");
        Assert.IsTrue(result.All(m =>
            m.FilePath?.Contains("AnimalService.cs") == true),
            "All results should be from the filtered file");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_WhenIncludingInterfaces_Returns_InterfaceTypeKind()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, "SampleLib", 1, 100, includeInterfaces: true, excludeTestProjects: false, CancellationToken.None);

        var interfaces = result.Where(m => m.TypeKind == "Interface").ToList();
        Assert.IsTrue(interfaces.Count > 0, "Expected at least one interface when includeInterfaces=true.");
        Assert.IsTrue(interfaces.All(i => i.Lcom4Score == i.MethodCount),
            "Interface metrics should use trivial LCOM4 equal to method count.");
    }

    [TestMethod]
    public async Task FindSharedMembers_Supports_StaticClasses()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var staticFilePath = Path.Combine(copiedRoot, "SampleLib", "StaticUtility.cs");
            await File.WriteAllTextAsync(staticFilePath, """
namespace SampleLib;

public static class StaticUtility
{
    private static int _counter;

    public static void Increment()
    {
        _counter++;
    }

    public static int Read()
    {
        return _counter;
    }
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var shared = await CohesionAnalysisService.FindSharedMembersAsync(
                workspaceId,
                SymbolLocator.ByMetadataName("SampleLib.StaticUtility"),
                CancellationToken.None);

            Assert.IsTrue(shared.Any(m => m.MemberName == "_counter"),
                "Expected static private field '_counter' to be reported as shared by multiple public static methods.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
