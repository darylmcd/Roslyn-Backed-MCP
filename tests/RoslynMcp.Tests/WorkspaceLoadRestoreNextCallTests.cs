using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Wire contract for the additive structured <c>nextCall</c> on <c>workspace_load</c> /
/// <c>workspace_reload</c> results, asserted on the real serializer's JSON for both the lean and
/// verbose projections.
/// </summary>
[TestClass]
public sealed class WorkspaceLoadRestoreNextCallTests
{
    private const string WorkspaceId = "ws-restore-next-call";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SerializeWorkspaceLoadResult_RestoreRequired_EmitsWorkspaceReloadNextCall(bool verbose)
    {
        using var doc = JsonDocument.Parse(
            WorkspaceTools.SerializeWorkspaceLoadResult(CreateStatus(restoreRequired: true), verbose, prewarmResult: null));

        var nextCall = doc.RootElement.GetProperty("nextCall");
        Assert.AreEqual("workspace_reload", nextCall.GetProperty("tool").GetString());
        var arguments = nextCall.GetProperty("arguments");
        Assert.AreEqual(WorkspaceId, arguments.GetProperty("workspaceId").GetString());
        Assert.IsTrue(arguments.GetProperty("autoRestore").GetBoolean());
    }

    [TestMethod]
    public void SerializeWorkspaceLoadResult_NextCall_IdenticalInLeanAndVerbose()
    {
        var status = CreateStatus(restoreRequired: true);
        using var lean = JsonDocument.Parse(WorkspaceTools.SerializeWorkspaceLoadResult(status, verbose: false, prewarmResult: null));
        using var verboseDoc = JsonDocument.Parse(WorkspaceTools.SerializeWorkspaceLoadResult(status, verbose: true, prewarmResult: null));

        Assert.AreEqual(
            lean.RootElement.GetProperty("nextCall").GetRawText(),
            verboseDoc.RootElement.GetProperty("nextCall").GetRawText());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SerializeWorkspaceLoadResult_RestoreNotRequired_OmitsNextCall(bool verbose)
    {
        using var doc = JsonDocument.Parse(
            WorkspaceTools.SerializeWorkspaceLoadResult(CreateStatus(restoreRequired: false), verbose, prewarmResult: null));

        Assert.IsFalse(doc.RootElement.TryGetProperty("nextCall", out _),
            "nextCall must be omitted when no restore is required.");
    }

    [TestMethod]
    public void SerializeWorkspaceLoadResult_RestoreRequiredWithPrewarm_CarriesBothNextCallAndPrewarm()
    {
        var prewarm = new WorkspaceWarmResult(WorkspaceId, ["Sample"], ElapsedMs: 1, ColdCompilationCount: 1);
        using var doc = JsonDocument.Parse(
            WorkspaceTools.SerializeWorkspaceLoadResult(CreateStatus(restoreRequired: true), verbose: false, prewarm));

        Assert.IsTrue(doc.RootElement.TryGetProperty("nextCall", out _));
        Assert.AreEqual(WorkspaceId, doc.RootElement.GetProperty("prewarm").GetProperty("workspaceId").GetString());
    }

    [TestMethod]
    public void SerializeWorkspaceLoadResult_PrewarmWithoutRestore_OmitsNextCall()
    {
        var prewarm = new WorkspaceWarmResult(WorkspaceId, ["Sample"], ElapsedMs: 1, ColdCompilationCount: 1);
        using var doc = JsonDocument.Parse(
            WorkspaceTools.SerializeWorkspaceLoadResult(CreateStatus(restoreRequired: false), verbose: false, prewarm));

        Assert.IsFalse(doc.RootElement.TryGetProperty("nextCall", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("prewarm", out _));
    }

    [TestMethod]
    public void LeanRestoreHint_RestoreRequired_DescribesMissingAssetsAndPointsAtAutoRestore()
    {
        var hint = WorkspaceStatusSummaryDto.From(CreateStatus(restoreRequired: true)).RestoreHint;

        Assert.IsNotNull(hint);
        StringAssert.Contains(hint, "missing or out of date");
        StringAssert.Contains(hint, "dotnet restore");
        StringAssert.Contains(hint, "autoRestore=true");
        Assert.IsFalse(hint.Contains("inputs changed", StringComparison.OrdinalIgnoreCase),
            "The hint must not claim inputs changed: restore is also required when project.assets.json is missing.");
    }

    private static WorkspaceStatusDto CreateStatus(bool restoreRequired) =>
        new(
            WorkspaceId: WorkspaceId,
            LoadedPath: @"C:\repo\Sample.slnx",
            WorkspaceVersion: 1,
            SnapshotToken: $"{WorkspaceId}:1",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 1,
            DocumentCount: 1,
            Projects:
            [
                new ProjectStatusDto(
                    Name: "Sample",
                    FilePath: @"C:\repo\Sample\Sample.csproj",
                    DocumentCount: 1,
                    ProjectReferences: [],
                    TargetFrameworks: ["net10.0"],
                    IsTestProject: false,
                    AssemblyName: "Sample",
                    OutputType: "Library"),
            ],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: [],
            RestoreRequired: restoreRequired);
}
