using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceStatusSummaryDtoTests
{
    // Path.GetFileName needs a platform-native separator — a Windows-literal `C:\work\...`
    // is a single filename on Linux, which made From_NoErrors_NotStale_IsReady fail on CI.
    private static readonly string SampleLoadedPath = Path.Combine("work", "SampleSolution.slnx");

    private static WorkspaceStatusDto BaselineStatus(IReadOnlyList<DiagnosticDto> diagnostics, bool isStale = false) =>
        new(
            WorkspaceId: "abc",
            LoadedPath: SampleLoadedPath,
            WorkspaceVersion: 1,
            SnapshotToken: "snap",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 2,
            DocumentCount: 10,
            Projects: [],
            IsLoaded: true,
            IsStale: isStale,
            WorkspaceDiagnostics: diagnostics);

    [TestMethod]
    public void From_NoErrors_NotStale_IsReady()
    {
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus([]));
        Assert.IsTrue(summary.IsReady);
        Assert.IsFalse(summary.IsStale);
        Assert.AreEqual(0, summary.WorkspaceErrorCount);
        Assert.AreEqual("SampleSolution.slnx", summary.SolutionFileName);
        Assert.IsNull(summary.RestoreHint);
    }

    [TestMethod]
    public void From_Stale_NotReady()
    {
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus([], isStale: true));
        Assert.IsFalse(summary.IsReady);
        Assert.IsTrue(summary.IsStale);
    }

    [TestMethod]
    public void From_WorkspaceError_NotReady()
    {
        var diagnostics = new[]
        {
            new DiagnosticDto("W1", "fail", "Error", "Workspace", null, null, null, null, null),
        };
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus(diagnostics));
        Assert.IsFalse(summary.IsReady);
        Assert.AreEqual(1, summary.WorkspaceErrorCount);
    }

    [TestMethod]
    public void From_ManySuspiciousMessages_RestoreHint()
    {
        var diagnostics = new DiagnosticDto[]
        {
            new("X", "Type 'A' could not be found", "Error", "Workspace", null, null, null, null, null),
            new("X", "Type 'B' could not be found", "Error", "Workspace", null, null, null, null, null),
            new("X", "Type 'C' could not be found", "Error", "Workspace", null, null, null, null, null),
        };
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus(diagnostics));
        Assert.IsNotNull(summary.RestoreHint);
        StringAssert.Contains(summary.RestoreHint, "dotnet restore");
    }

    /// <summary>
    /// Regression for `workspace-load-isready-misreports-unresolved-analyzers` (P4) and
    /// `severity-flag-soft-inconsistency-does-not-block-work` (P4): Jellyfin 2026-04-16
    /// reproduced `isReady=true` with 40 `WORKSPACE_UNRESOLVED_ANALYZER` warnings because
    /// the old `isReady` formula only checked errors, not warnings. Analyzer-driven tools
    /// (find_unused_symbols, project_diagnostics) silently under-reported every finding.
    /// Post-fix: `analyzersReady` is its own field; `isReady` requires both.
    /// </summary>
    [TestMethod]
    public void From_UnresolvedAnalyzerWarning_AnalyzersReadyFalse_AndIsReadyFalse()
    {
        var diagnostics = new DiagnosticDto[]
        {
            new("WORKSPACE_UNRESOLVED_ANALYZER", "Failed to load analyzer reference 'Foo'", "Warning", "Workspace", null, null, null, null, null),
        };
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus(diagnostics));
        Assert.IsFalse(summary.AnalyzersReady, "AnalyzersReady must be false when an unresolved-analyzer warning is present");
        Assert.IsFalse(summary.IsReady, "IsReady must require AnalyzersReady");
        Assert.IsNotNull(summary.RestoreHint);
        StringAssert.Contains(summary.RestoreHint, "analyzer reference");
    }

    /// <summary>
    /// Regression for `dr-9-7-reports-during-refactoring-transitions-but-the-w` (P3):
    /// NetworkDocumentation §9.7 reported `isReady=false` with `workspaceErrorCount=1`
    /// during transient stale after applies + revert while tools still operated. The
    /// transient-stale case (stale + zero errors) should now produce a distinct hint
    /// telling the caller to retry rather than reload.
    /// </summary>
    [TestMethod]
    public void From_StaleWithoutErrors_TransientHint()
    {
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus([], isStale: true));
        Assert.IsFalse(summary.IsReady);
        Assert.IsTrue(summary.IsStale);
        Assert.AreEqual(0, summary.WorkspaceErrorCount);
        Assert.IsNotNull(summary.RestoreHint, "stale workspace with no errors must produce a transient-retry hint");
        StringAssert.Contains(summary.RestoreHint, "transient");
        StringAssert.Contains(summary.RestoreHint, "Retry");
    }

    /// <summary>
    /// Healthy workspace (no errors, no warnings, not stale) keeps the existing baseline:
    /// every readiness flag is true, no restore hint emitted. Guards against accidentally
    /// firing the new restore-hint paths in the happy path.
    /// </summary>
    [TestMethod]
    public void From_Healthy_AllFlagsTrue_NoHint()
    {
        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus([]));
        Assert.IsTrue(summary.IsReady);
        Assert.IsTrue(summary.AnalyzersReady);
        Assert.IsFalse(summary.IsStale);
        Assert.IsNull(summary.RestoreHint);
    }
}
