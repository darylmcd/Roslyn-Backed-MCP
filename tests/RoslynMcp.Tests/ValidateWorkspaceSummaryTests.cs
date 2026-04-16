using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `validate-workspace-output-cap-summary-mode` (P3): Jellyfin
/// experimental promotion 2026-04-16 §8 reproduced `validate_workspace runTests=false`
/// returning 135 KB on a 40-project solution. New `summary: bool = false` parameter
/// drops <c>ErrorDiagnostics</c> + <c>DiscoveredTests</c> when true; counts +
/// <c>OverallStatus</c> still surface the verdict.
/// </summary>
[TestClass]
public sealed class ValidateWorkspaceSummaryTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static WorkspaceValidationService _validationService = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        _validationService = new WorkspaceValidationService(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ValidateAsync_DefaultMode_PopulatesErrorDiagnosticsAndDiscoveredTests()
    {
        var result = await _validationService.ValidateAsync(
            WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.ErrorDiagnostics, "default mode must populate ErrorDiagnostics list (may be empty if workspace is clean)");
        Assert.IsNotNull(result.DiscoveredTests, "default mode must populate DiscoveredTests list (may be empty)");
    }

    [TestMethod]
    public async Task ValidateAsync_SummaryMode_DropsHeavyLists_ButKeepsStatusAndCounts()
    {
        var result = await _validationService.ValidateAsync(
            WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None, summary: true);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ErrorDiagnostics.Count, "summary=true must emit empty ErrorDiagnostics");
        Assert.AreEqual(0, result.DiscoveredTests.Count, "summary=true must emit empty DiscoveredTests");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.OverallStatus), "OverallStatus must still surface in summary mode");
        Assert.IsNotNull(result.CompileResult, "CompileResult must still be populated (it carries its own bounded summary)");
    }

    [TestMethod]
    public async Task ValidateAsync_SummaryMode_ResponseSmallerThanFullMode()
    {
        var fullResult = await _validationService.ValidateAsync(
            WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None, summary: false);
        var summaryResult = await _validationService.ValidateAsync(
            WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None, summary: true);

        var fullJson = System.Text.Json.JsonSerializer.Serialize(fullResult);
        var summaryJson = System.Text.Json.JsonSerializer.Serialize(summaryResult);

        // summary <= full (strict less-than only when there's actual content to drop;
        // SampleSolution may be small enough that the difference is in the noise).
        Assert.IsTrue(summaryJson.Length <= fullJson.Length,
            $"summary JSON must not exceed full JSON; summary={summaryJson.Length}, full={fullJson.Length}");
    }
}
