using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for the v2 top-10 remediation pass (post-PR-#150). Smaller, focused
/// asserts live alongside their feature; this file batches the cross-cutting smoke tests.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class Top10V2RegressionTests : IsolatedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath);
    }

    // ── solution-project-index-by-name ──

    [TestMethod]
    public void GetProject_ResolvesByName()
    {
        var project = WorkspaceManager.GetProject(_workspaceId, "SampleLib");
        Assert.IsNotNull(project, "GetProject must resolve SampleLib by simple name.");
        Assert.AreEqual("SampleLib", project.Name);
    }

    [TestMethod]
    public void GetProject_ResolvesByFilePath()
    {
        // Resolve via name first, then look up by the resolved file path.
        var byName = WorkspaceManager.GetProject(_workspaceId, "SampleLib");
        Assert.IsNotNull(byName?.FilePath);
        var byPath = WorkspaceManager.GetProject(_workspaceId, byName!.FilePath!);
        Assert.IsNotNull(byPath, "GetProject must resolve by absolute file path too.");
        Assert.AreSame(byName, byPath);
    }

    [TestMethod]
    public void GetProject_UnknownReturnsNull()
    {
        Assert.IsNull(WorkspaceManager.GetProject(_workspaceId, "DefinitelyNotARealProject"));
    }

    // ── di-registrations-scan-caching ──

    [TestMethod]
    public async Task GetDiRegistrations_RepeatCallSameVersion_ReturnsCachedReference()
    {
        var first = await DiRegistrationService.GetDiRegistrationsAsync(_workspaceId, projectFilter: null, CancellationToken.None);
        var second = await DiRegistrationService.GetDiRegistrationsAsync(_workspaceId, projectFilter: null, CancellationToken.None);
        Assert.AreSame(first, second, "Second call with same filter on same workspaceVersion must return the cached list.");
    }

    // ── source-file-resource-line-range-parity ──

    [TestMethod]
    public async Task SourceFileLinesResource_SlicesAndPrependsMarker()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var content = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "5-10", CancellationToken.None);

        StringAssert.Contains(content, "// roslyn://", "Marker comment must prefix the slice.");
        StringAssert.Contains(content, "lines/5-", "Marker must reference the requested range.");

        // Body should be much smaller than the full file (full file is ~30 lines).
        var bodyLineCount = content.Split('\n').Length;
        Assert.IsTrue(bodyLineCount < 20, $"Slice should be small (<20 lines including marker); got {bodyLineCount}.");
    }

    // dr-9-13-flag-resource-invalid-range-resource-returns-ge:
    // Pre-fix: invalid ranges threw McpToolException → framework surfaced -32603 JSON-RPC.
    // Post-fix: the handler is wrapped in ToolErrorHandler.ExecuteResourceAsync which returns
    // a structured JSON error envelope (category, message, tool) for every input-validation
    // failure.
    [TestMethod]
    public async Task SourceFileLinesResource_EndBeforeStart_ReturnsStructuredInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var json = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "10-5", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}",
            doc.RootElement.GetProperty("tool").GetString(),
            "Resource URI template must populate the tool field, not 'unknown'.");
    }

    [TestMethod]
    public async Task SourceFileLinesResource_NonNumericRange_ReturnsStructuredInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var json = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "abc-def", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
    }

    [TestMethod]
    public async Task SourceFileLinesResource_StartLinePastEof_ReturnsStructuredInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        // AnimalService.cs is ~30 lines; 9999 is safely past EOF.
        var json = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "9999-10000", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString() ?? string.Empty,
            "past the end",
            "Error message should explain the startLine-past-EOF condition.");
    }

    // ── apply-with-verify-and-rollback ──

    [TestMethod]
    public async Task ApplyWithVerify_GoodPreview_ReturnsApplied()
    {
        // Use organize_usings on AnimalService.cs as a known-clean refactor that should not
        // introduce errors. The workspace is shared, so we only verify status, not actual diff.
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var animalServicePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, animalServicePath, CancellationToken.None);

        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, RefactoringService, CompileCheckService, UndoService, PreviewStore,
            preview.PreviewToken, rollbackOnError: true, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.IsTrue(status is "applied" or "rolled_back",
            $"Expected applied or rolled_back; got {status}. Full JSON: {json}");
    }
}
