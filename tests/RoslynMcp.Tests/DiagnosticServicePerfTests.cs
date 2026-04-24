using System.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>
/// diagnostic-details-perf-budget-overrun: guards the single-document short-circuit in
/// <see cref="RoslynMcp.Roslyn.Services.DiagnosticService.GetDiagnosticDetailsAsync"/>
/// against future regressions. The cold path previously ran a solution-wide compile +
/// analyzer pass (~16s on large solutions); the fast path scopes to the supplied
/// document and must stay well under the perf budget.
/// <para>
/// Uses an isolated workspace copy (not the shared sample workspace) so the mid-test
/// <see cref="RoslynMcp.Roslyn.Services.WorkspaceManager.ReloadAsync"/> calls do not
/// race with other test classes that read from <see cref="SharedWorkspaceTestBase"/>.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class DiagnosticServicePerfTests : TestBase
{
    // The plan calls for a <2s budget on a representative solution. CI runners (especially
    // GitHub Actions hosted Windows runners) frequently run ~2x slower than local hardware
    // for first-cold compilation + analyzer setup, so the guard here is set at 5s to catch
    // the real regression (the 16s solution-wide scan reported on the row) without flaking
    // on legitimate runner variance. The measured local latency is logged via TestContext
    // so the margin between observed and budget stays visible.
    private const int ColdPathPerfBudgetMs = 5000;

    private static string _workspaceId = null!;
    private static string _copiedSolutionRoot = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var copiedSolutionPath = CreateSampleSolutionCopy();
        _copiedSolutionRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var copied = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        _workspaceId = copied.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        try { DeleteDirectoryIfExists(_copiedSolutionRoot); }
        finally { DisposeServices(); }
    }

    [TestMethod]
    public async Task GetDiagnosticDetailsAsync_ColdPath_StaysUnderPerfBudget()
    {
        var solution = WorkspaceManager.GetCurrentSolution(_workspaceId);
        var animalServiceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == "AnimalService.cs");

        // Force a cold path (no warm cache) by reloading the workspace. Reload invalidates
        // both the per-project diagnostic cache and the full-result cache, so the next call
        // to GetDiagnosticDetailsAsync cannot hit the warm-cache fast path and must exercise
        // the single-document short-circuit.
        await WorkspaceManager.ReloadAsync(_workspaceId, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var details = await DiagnosticService.GetDiagnosticDetailsAsync(
            _workspaceId,
            diagnosticId: "CS8019",
            filePath: animalServiceFile.FilePath!,
            line: 1,
            column: 1,
            CancellationToken.None);
        stopwatch.Stop();

        Assert.IsNotNull(details, "CS8019 must be found on a cold cache via the single-document path.");
        Assert.AreEqual("CS8019", details.Diagnostic.Id);

        // Record the measured latency so future regressions show up in the test output.
        TestContext?.WriteLine($"Cold-path diagnostic_details latency: {stopwatch.ElapsedMilliseconds} ms (budget: {ColdPathPerfBudgetMs} ms)");

        Assert.IsTrue(
            stopwatch.ElapsedMilliseconds < ColdPathPerfBudgetMs,
            $"Cold-path GetDiagnosticDetailsAsync took {stopwatch.ElapsedMilliseconds} ms; budget is {ColdPathPerfBudgetMs} ms. " +
            "The single-document short-circuit in DiagnosticService.FindDiagnosticInDocumentAsync may have regressed back to a solution-wide scan.");
    }

    [TestMethod]
    public async Task GetDiagnosticDetailsAsync_FastPath_MatchesFullScanForSemanticDiagnostic()
    {
        // Parity test: the single-document short-circuit must find the same diagnostic that
        // the legacy solution-wide scan (still exercised by the warm cache + the fallback)
        // reports. CS0414 is a semantic-model diagnostic, not an analyzer one — it exercises
        // the Compilation.GetDiagnostics path inside FindDiagnosticInDocumentAsync.
        var solution = WorkspaceManager.GetCurrentSolution(_workspaceId);
        var probeFile = solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == "DiagnosticsProbe.cs");

        // Warm-cache path (list call populates the per-workspace Diagnostic cache used by
        // the fast warm path in GetDiagnosticDetailsAsync).
        _ = await DiagnosticService.GetDiagnosticsAsync(
            _workspaceId,
            projectFilter: null,
            fileFilter: null,
            severityFilter: null,
            diagnosticIdFilter: null,
            CancellationToken.None);

        var warm = await DiagnosticService.GetDiagnosticDetailsAsync(
            _workspaceId,
            diagnosticId: "CS0414",
            filePath: probeFile.FilePath!,
            line: 9,
            column: 24,
            CancellationToken.None);

        // Cold-cache path (exercises FindDiagnosticInDocumentAsync directly).
        await WorkspaceManager.ReloadAsync(_workspaceId, CancellationToken.None);

        var cold = await DiagnosticService.GetDiagnosticDetailsAsync(
            _workspaceId,
            diagnosticId: "CS0414",
            filePath: probeFile.FilePath!,
            line: 9,
            column: 24,
            CancellationToken.None);

        Assert.IsNotNull(warm, "Warm-path must find CS0414.");
        Assert.IsNotNull(cold, "Cold-path (single-document short-circuit) must find CS0414.");
        Assert.AreEqual(warm.Diagnostic.Id, cold.Diagnostic.Id);
        Assert.AreEqual(warm.Diagnostic.Severity, cold.Diagnostic.Severity);
        Assert.AreEqual(warm.Diagnostic.FilePath, cold.Diagnostic.FilePath);
        Assert.AreEqual(warm.Diagnostic.StartLine, cold.Diagnostic.StartLine);
        Assert.AreEqual(warm.Diagnostic.StartColumn, cold.Diagnostic.StartColumn);
        Assert.AreEqual(warm.Description, cold.Description);
        Assert.AreEqual(warm.HelpLinkUri, cold.HelpLinkUri);
        Assert.AreEqual(warm.GuidanceMessage, cold.GuidanceMessage);
        Assert.AreEqual(warm.SupportedFixes.Count, cold.SupportedFixes.Count);
    }

    public TestContext? TestContext { get; set; }
}
