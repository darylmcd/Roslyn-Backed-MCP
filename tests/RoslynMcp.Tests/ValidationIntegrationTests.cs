using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public class ValidationIntegrationTests : SharedWorkspaceTestBase
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
    public void Workspace_Status_Identifies_Test_Project_Metadata()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var testProject = status.Projects.FirstOrDefault(project => project.Name == "SampleLib.Tests");

        Assert.IsNotNull(testProject, "SampleLib.Tests project not found.");
        Assert.IsTrue(testProject.IsTestProject, "SampleLib.Tests should be recognized as a test project.");
        Assert.AreEqual("Library", testProject.OutputType);
        Assert.AreEqual("SampleLib.Tests", testProject.AssemblyName);
    }

    [TestMethod]
    public async Task Test_Discover_Returns_Test_Methods_For_Test_Project()
    {
        var discovery = await TestDiscoveryService.DiscoverTestsAsync(WorkspaceId, CancellationToken.None);

        Assert.IsTrue(discovery.TestProjects.Count > 0, "Expected at least one discovered test project.");
        var testProject = discovery.TestProjects.FirstOrDefault(project => project.ProjectName == "SampleLib.Tests");
        Assert.IsNotNull(testProject, "Expected SampleLib.Tests to be discovered.");
        Assert.IsTrue(testProject.Tests.Any(test => test.DisplayName == "CountAnimals_Returns_Total_Count"));
        Assert.IsTrue(testProject.Tests.Any(test => test.DisplayName == "GetAllAnimals_Returns_Dog_And_Cat"));
    }

    [TestMethod]
    public async Task Build_Workspace_Returns_Structured_Success()
    {
        var result = await BuildService.BuildWorkspaceAsync(WorkspaceId, CancellationToken.None);

        Assert.IsTrue(result.Execution.Succeeded, result.Execution.StdErr);
        Assert.AreEqual(0, result.Execution.ExitCode);
        Assert.AreEqual(0, result.ErrorCount, "Expected no build errors for the sample solution (warnings may appear in some SDK configurations).");
    }

    [TestMethod]
    public async Task Test_Run_Returns_Structured_Success()
    {
        var result = await TestRunnerService.RunTestsAsync(
            WorkspaceId,
            projectName: "SampleLib.Tests",
            filter: null,
            CancellationToken.None);

        Assert.IsTrue(result.Execution.Succeeded, result.Execution.StdErr);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(2, result.Passed);
        Assert.AreEqual(0, result.Failed);
    }

    [TestMethod]
    public async Task Build_Workspace_Returns_Parsed_Diagnostics_For_Broken_Solution()
    {
        var buildFailureWorkspace = await WorkspaceManager.LoadAsync(BuildFailureSolutionPath, CancellationToken.None);

        var result = await BuildService.BuildWorkspaceAsync(buildFailureWorkspace.WorkspaceId, CancellationToken.None);

        Assert.IsFalse(result.Execution.Succeeded, "Build should fail for the broken fixture.");
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0103"));
    }

    [TestMethod]
    public async Task Test_Run_Parses_Failing_Test_Results_From_Isolated_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var testFilePath = Path.Combine(copiedRoot, "SampleLib.Tests", "AnimalServiceTests.cs");

        try
        {
            var originalContents = await File.ReadAllTextAsync(testFilePath, CancellationToken.None);
            var failingContents = originalContents.Replace("Assert.AreEqual(2, count);", "Assert.AreEqual(3, count);");
            await File.WriteAllTextAsync(testFilePath, failingContents, CancellationToken.None);

            var copiedWorkspace = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var result = await TestRunnerService.RunTestsAsync(
                copiedWorkspace.WorkspaceId,
                projectName: "SampleLib.Tests",
                filter: null,
                CancellationToken.None);

            Assert.IsFalse(result.Execution.Succeeded, "Test run should report failure for the modified fixture.");
            Assert.AreEqual(1, result.Failed);
            Assert.IsTrue(
                result.Failures.Any(IsCountAnimalsFailure),
                $"Expected failing test CountAnimals_Returns_Total_Count in failures. Got: {string.Join("; ", result.Failures.Select(f => $"{f.DisplayName} | {f.FullyQualifiedName}"))}");

            static bool IsCountAnimalsFailure(TestFailureDto failure) =>
                string.Equals(failure.DisplayName, "CountAnimals_Returns_Total_Count", StringComparison.Ordinal)
                || (failure.FullyQualifiedName?.Contains("CountAnimals_Returns_Total_Count", StringComparison.Ordinal) ?? false);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
