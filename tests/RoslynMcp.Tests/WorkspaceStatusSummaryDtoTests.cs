using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceStatusSummaryDtoTests
{
    private static WorkspaceStatusDto BaselineStatus(IReadOnlyList<DiagnosticDto> diagnostics, bool isStale = false) =>
        new(
            WorkspaceId: "abc",
            LoadedPath: @"C:\work\SampleSolution.slnx",
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
}
