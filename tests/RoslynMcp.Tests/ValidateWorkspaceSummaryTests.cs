using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `validate-workspace-output-cap-summary-mode` (P3): Jellyfin
/// experimental promotion 2026-04-16 §8 reproduced `validate_workspace runTests=false`
/// returning 135 KB on a 40-project solution. New `summary: bool = false` parameter
/// drops <c>ErrorDiagnostics</c> + <c>DiscoveredTests</c> when true; counts +
/// <c>OverallStatus</c> still surface the verdict.
/// </summary>
[DoNotParallelize]
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

    // dr-9-8-bug-validate-fabricated-accepts-fabricated-silen: fabricated / nonexistent paths
    // used to be silently dropped by the downstream test-discovery stage; the response gave
    // no signal that the caller's scope was ignored. UnknownFilePaths now surfaces them.
    [TestMethod]
    public async Task ValidateAsync_FabricatedChangedFilePaths_SurfacedInUnknownFilePaths()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "definitely-not-in-workspace-" + Guid.NewGuid().ToString("N") + ".cs");

        var result = await _validationService.ValidateAsync(
            WorkspaceId,
            changedFilePaths: [fakePath],
            runTests: false,
            CancellationToken.None);

        Assert.IsNotNull(result.UnknownFilePaths, "UnknownFilePaths must be non-null (empty list when all paths resolve).");
        Assert.AreEqual(1, result.UnknownFilePaths.Count,
            $"The fabricated path must surface as unknown; got {result.UnknownFilePaths.Count} entries.");
        Assert.AreEqual(fakePath, result.UnknownFilePaths[0]);
        Assert.AreEqual(0, result.ChangedFilePaths.Count,
            "A fabricated path must NOT leak into ChangedFilePaths — that field is the known set.");
    }

    [TestMethod]
    public async Task ValidateAsync_MixedRealAndFabricatedPaths_PartitionsCorrectly()
    {
        var realPath = FindDocumentPath("AnimalService.cs");
        var fakePath = Path.Combine(Path.GetTempPath(), "imaginary-" + Guid.NewGuid().ToString("N") + ".cs");

        var result = await _validationService.ValidateAsync(
            WorkspaceId,
            changedFilePaths: [realPath, fakePath],
            runTests: false,
            CancellationToken.None);

        Assert.AreEqual(1, result.ChangedFilePaths.Count, "Known path must appear in ChangedFilePaths.");
        Assert.AreEqual(1, result.UnknownFilePaths.Count, "Fabricated path must appear in UnknownFilePaths.");
        Assert.AreEqual(fakePath, result.UnknownFilePaths[0]);
    }

    [TestMethod]
    public async Task ValidateAsync_NullChangedFilePaths_NoUnknownSurfaced()
    {
        // Change-tracker fallback path: every recorded file was materialized from a Roslyn
        // document mutation so there can't be unknown entries.
        var result = await _validationService.ValidateAsync(
            WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None);

        Assert.IsNotNull(result.UnknownFilePaths);
        Assert.AreEqual(0, result.UnknownFilePaths.Count,
            "Change-tracker entries are materialized from Roslyn ops so none should ever be unknown.");
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;
        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }
}
