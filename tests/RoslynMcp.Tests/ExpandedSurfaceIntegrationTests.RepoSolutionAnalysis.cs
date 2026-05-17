namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
[TestCategory("RepoSolution")]
public sealed class ExpandedSurfaceIntegrationTests_RepoSolutionAnalysis : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Reflection_And_Di_Analysis_Run_On_Repo_Solution()
    {
        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var status = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var reflectionUsages = await CodePatternAnalyzer.FindReflectionUsagesAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);
            var diRegistrations = await DiRegistrationService.GetDiRegistrationsAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);

            Assert.IsTrue(reflectionUsages.Count > 0, "Expected reflection analysis to complete with results on the repo solution.");
            Assert.IsTrue(diRegistrations.Any(registration =>
                registration.FilePath.EndsWith("ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)),
                "Expected DI registrations from ServiceCollectionExtensions.cs.");
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }
}
