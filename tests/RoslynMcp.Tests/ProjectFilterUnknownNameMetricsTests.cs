namespace RoslynMcp.Tests;

/// <summary>
/// project-filter-unknown-name-metrics-services: <c>get_complexity_metrics</c>,
/// <c>get_cohesion_metrics</c>, <c>get_coupling_metrics</c> and <c>get_namespace_dependencies</c>
/// must reject a non-blank <c>projectName</c> that matches no loaded project (ArgumentException,
/// ParamName <c>projectName</c>, redacted by the host to the compile_check InvalidArgument envelope)
/// instead of returning an empty success-shaped result. A known name keeps returning results.
/// </summary>
[TestClass]
public sealed class ProjectFilterUnknownNameMetricsTests : SharedWorkspaceTestBase
{
    private const string UnknownProject = "NoSuchProject.Typo";

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
    public async Task GetComplexityMetrics_UnknownProject_ThrowsInvalidProjectName()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CodeMetricsService.GetComplexityMetricsAsync(
                WorkspaceId, filePath: null, filePaths: null, projectFilter: UnknownProject,
                minComplexity: null, limit: 50, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task GetComplexityMetrics_KnownProject_ReturnsResults()
    {
        var result = await CodeMetricsService.GetComplexityMetricsAsync(
            WorkspaceId, filePath: null, filePaths: null, projectFilter: "SampleLib",
            minComplexity: null, limit: 50, CancellationToken.None);

        Assert.IsTrue(result.Count > 0, "Known project must still yield complexity metrics.");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_UnknownProject_ThrowsInvalidProjectName()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CohesionAnalysisService.GetCohesionMetricsAsync(
                WorkspaceId, filePath: null, projectFilter: UnknownProject, minMethods: 1, limit: 50,
                includeInterfaces: true, excludeTestProjects: false, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task GetCohesionMetrics_KnownProject_ReturnsResults()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, filePath: null, projectFilter: "SampleLib", minMethods: 1, limit: 50,
            includeInterfaces: true, excludeTestProjects: false, CancellationToken.None);

        Assert.IsTrue(result.Count > 0, "Known project must still yield cohesion metrics.");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_TestProjectOnlyFilter_WithExcludeTestProjects_ResolvesThenFiltersToEmpty()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, filePath: null, projectFilter: "SampleLib.Tests", minMethods: 1, limit: 50,
            includeInterfaces: true, excludeTestProjects: true, CancellationToken.None);

        Assert.AreEqual(0, result.Count,
            "A filter naming only a test project resolves, then excludeTestProjects removes it (no throw).");
    }

    [TestMethod]
    public async Task GetCouplingMetrics_UnknownProject_ThrowsInvalidProjectName()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CouplingAnalysisService.GetCouplingMetricsAsync(
                WorkspaceId, projectFilter: UnknownProject, limit: 50,
                excludeTestProjects: false, includeInterfaces: false, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task GetCouplingMetrics_KnownProject_ReturnsResults()
    {
        var result = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId, projectFilter: "SampleLib", limit: 50,
            excludeTestProjects: false, includeInterfaces: false, CancellationToken.None);

        Assert.IsTrue(result.Count > 0, "Known project must still yield coupling metrics.");
    }

    [TestMethod]
    public async Task GetCouplingMetrics_TestProjectOnlyFilter_WithExcludeTestProjects_ResolvesThenFiltersToEmpty()
    {
        var result = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId, projectFilter: "SampleLib.Tests", limit: 50,
            excludeTestProjects: true, includeInterfaces: false, CancellationToken.None);

        Assert.AreEqual(0, result.Count,
            "A filter naming only a test project resolves, then excludeTestProjects removes it (no throw).");
    }

    [TestMethod]
    public async Task GetNamespaceDependencies_UnknownProject_ThrowsInvalidProjectName()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            NamespaceDependencyService.GetNamespaceDependenciesAsync(
                WorkspaceId, projectFilter: UnknownProject, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task GetNamespaceDependencies_KnownProject_ReturnsGraph()
    {
        var result = await NamespaceDependencyService.GetNamespaceDependenciesAsync(
            WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        Assert.IsTrue(result.Nodes.Count > 0, "Known project must still yield namespace nodes.");
    }

    private static void AssertProjectNameRejection(ArgumentException ex)
    {
        Assert.AreEqual("projectName", ex.ParamName);
        StringAssert.Contains(ex.Message, "matched 0 projects");
    }
}
