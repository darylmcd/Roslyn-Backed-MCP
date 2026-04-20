using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #6 — regression guard for `compile-check-zero-projects-claimed-success-after-reload`.
/// SampleSolution audit §9.8 repro: compile_check returned success:true, completedProjects:0
/// with _meta.staleAction:auto-reloaded while the workspace still had real errors. The fix
/// adds a completedProjects&gt;0 precondition to Success and a structured actionable hint.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class CompileCheckZeroProjectsTests : SharedWorkspaceTestBase
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
    public async Task CompileCheck_With_NonMatching_ProjectFilter_Is_NotSuccess()
    {
        var options = new CompileCheckOptions(
            ProjectFilter: "DoesNotExist_Item6_Guard",
            EmitValidation: false,
            SeverityFilter: null,
            FileFilter: null,
            Offset: 0,
            Limit: 50);

        var result = await CompileCheckService.CheckAsync(WorkspaceId, options, CancellationToken.None);

        Assert.AreEqual(0, result.CompletedProjects, "No projects should have been evaluated.");
        Assert.AreEqual(0, result.TotalProjects, "The filter should have matched zero projects.");
        Assert.IsFalse(result.Success, "Success must be false when zero projects are evaluated — previously this returned true, producing the false-green.");
        Assert.IsNotNull(result.RestoreHint, "RestoreHint must be populated with an actionable zero-projects message.");
        Assert.IsTrue(
            result.RestoreHint!.Contains("projectFilter", StringComparison.OrdinalIgnoreCase) ||
            result.RestoreHint.Contains("0 projects", StringComparison.OrdinalIgnoreCase),
            $"Zero-projects hint should reference the filter or the zero-project case. Got: {result.RestoreHint}");
    }

    [TestMethod]
    public async Task CompileCheck_With_Valid_Project_Returns_NonZero_Completed()
    {
        // Sanity regression: the normal path is unaffected by the Item #6 guard.
        var options = new CompileCheckOptions(
            ProjectFilter: null,
            EmitValidation: false,
            SeverityFilter: "Error",
            FileFilter: null,
            Offset: 0,
            Limit: 50);

        var result = await CompileCheckService.CheckAsync(WorkspaceId, options, CancellationToken.None);

        Assert.IsTrue(result.CompletedProjects > 0, $"Expected at least one project evaluated. Got CompletedProjects={result.CompletedProjects}");
        Assert.IsTrue(result.TotalProjects > 0, $"Expected at least one project in the filtered scope. Got TotalProjects={result.TotalProjects}");
        // Success depends on whether the sample workspace has errors; just verify the guard
        // does not regress the normal success computation.
        if (result.ErrorCount == 0)
        {
            Assert.IsTrue(result.Success, "With zero errors and >0 completed projects the result must still report success.");
        }
    }
}
