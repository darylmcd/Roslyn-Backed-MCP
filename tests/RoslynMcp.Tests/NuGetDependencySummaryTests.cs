namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `get-nuget-dependencies-no-summary-mode` (P3): Jellyfin stress test
/// 2026-04-15 §4 reproduced `get_nuget_dependencies` returning ~102 KB on a 40-project
/// solution, exceeding the MCP cap. Added optional `summary: bool = false` to
/// <see cref="RoslynMcp.Roslyn.Services.NuGetDependencyService.GetNuGetDependenciesAsync"/>;
/// when true, only the compact per-package <c>NuGetPackageSummaryDto</c> list is
/// populated and the verbose Packages + Projects arrays are emitted as empty.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class NuGetDependencySummaryTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetNuGetDependencies_DefaultMode_PopulatesVerbosePackagesAndProjects()
    {
        var result = await NuGetDependencyService.GetNuGetDependenciesAsync(
            WorkspaceId, CancellationToken.None, summary: false);

        Assert.IsNotNull(result);
        Assert.IsNull(result.Summaries, "summary=false must NOT populate Summaries");
        // SampleSolution has at least one project, so Projects must be non-empty.
        Assert.IsTrue(result.Projects.Count > 0, "default mode must enumerate projects");
    }

    [TestMethod]
    public async Task GetNuGetDependencies_SummaryMode_PopulatesSummariesAndDropsVerboseArrays()
    {
        var result = await NuGetDependencyService.GetNuGetDependenciesAsync(
            WorkspaceId, CancellationToken.None, summary: true);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Summaries, "summary=true must populate Summaries");
        Assert.AreEqual(0, result.Packages.Count, "summary=true must emit empty Packages array");
        Assert.AreEqual(0, result.Projects.Count, "summary=true must emit empty Projects array");

        // Each summary entry must have non-empty PackageId and a sane DistinctVersionCount.
        foreach (var s in result.Summaries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.PackageId));
            Assert.IsTrue(s.ProjectCount >= 1, $"package {s.PackageId} must have at least one referencing project");
            Assert.IsTrue(s.DistinctVersionCount >= 1, $"package {s.PackageId} must have at least one distinct version");
        }
    }

    [TestMethod]
    public async Task GetNuGetDependencies_SummaryMode_ResponseSmallerThanFullMode()
    {
        var fullResult = await NuGetDependencyService.GetNuGetDependenciesAsync(
            WorkspaceId, CancellationToken.None, summary: false);
        var summaryResult = await NuGetDependencyService.GetNuGetDependenciesAsync(
            WorkspaceId, CancellationToken.None, summary: true);

        var fullJson = System.Text.Json.JsonSerializer.Serialize(fullResult);
        var summaryJson = System.Text.Json.JsonSerializer.Serialize(summaryResult);

        Assert.IsTrue(summaryJson.Length < fullJson.Length,
            $"summary JSON must be strictly smaller than full JSON; summary={summaryJson.Length}, full={fullJson.Length}");
    }
}
